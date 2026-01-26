using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Client;

namespace Collodion
{
    // Viewfinder state machine.
    //
    // Responsibilities:
    // - Detect camera + RMB/LMB input and manage viewfinder mode lifetime
    // - Schedule photo captures and trigger server notifications
    // - Apply zoom via the runtime FOV helpers (see CollodionModSystem.RuntimeFov.cs)
    //
    // Intentionally does NOT:
    // - Contain the reflection-heavy runtime-FOV binding
    // - Contain the reflection-heavy mouse state probing
    public partial class CollodionModSystem
    {
        private const float RmbReleaseGraceSeconds = 0.04f;
        private const float ViewfinderZoomMultiplier = 0.65f;

        private long viewfinderTickListenerId;
        private bool suppressViewfinderUntilRmbReleased;
        private bool captureInProgress;
        private float rmbUpSeconds;
        private bool lastLmbDown;
        private bool lastRmbDown;

        private long lastShutterGateChatMs;

        private bool f4TipShownThisViewfinder;
        private bool f4TipShownEver;

        private static readonly string[] ViewfinderFloatKeysToZoom = { "fieldOfView", "fpHandsFoV" };

        // Viewfinder effect: FOV zoom
        private readonly object viewfinderLock = new object();
        private int viewfinderDepth;
        private readonly Dictionary<string, float> viewfinderOldFloatSettings = new Dictionary<string, float>();
        private readonly Dictionary<string, float> viewfinderTargetFloatSettings = new Dictionary<string, float>();

        private Func<float>? runtimeFovGetter;
        private Action<float>? runtimeFovSetter;
        private float runtimeOldFov;
        private float runtimeTargetFov;
        private string? runtimeFovMechanism;
        private bool zoomMechanismTipShownThisViewfinder;

        public bool IsViewfinderActive
        {
            get
            {
                lock (viewfinderLock) return viewfinderDepth > 0;
            }
        }

        public void BeginViewfinderMode()
        {
            if (ClientApi == null) return;

            lock (viewfinderLock)
            {
                viewfinderDepth++;
                if (viewfinderDepth > 1) return;

                viewfinderOldFloatSettings.Clear();
                viewfinderTargetFloatSettings.Clear();

                runtimeFovGetter = null;
                runtimeFovSetter = null;
                runtimeFovMechanism = null;
                zoomMechanismTipShownThisViewfinder = false;

                f4TipShownThisViewfinder = false;
                MaybeShowF4GuiLessTip();

                // Preferred zoom: Spyglass-style patch of Set3DProjection(float,float).
                // This is the most reliable way to force a visual zoom on clients where settings
                // changes do not apply live.
                if (TryEnsureHarmonyProjectionZoomPatch())
                {
                    runtimeFovMechanism = GetZoomMechanismForTip() ?? "Harmony: Set3DProjection";
                    return;
                }

                // Zoom by adjusting runtime camera FOV if possible (preferred).
                // Some VS versions don't apply Settings.Float["fieldOfView"] live.
                if (TryBindRuntimeFovAccessors(out var getter, out var setter, out var mechanism))
                {
                    runtimeFovGetter = getter;
                    runtimeFovSetter = setter;
                    runtimeFovMechanism = mechanism;

                    float old = SafeGetFov(getter, fallback: 70f);
                    runtimeOldFov = old;

                    float newFov = ClampFov(old * ViewfinderZoomMultiplier, old);
                    runtimeTargetFov = newFov;
                    SafeSetFov(setter, newFov);
                    return;
                }

                // Fallback: zoom via client settings keys.
                foreach (string key in ViewfinderFloatKeysToZoom)
                {
                    if (!ClientApi.Settings.Float.Exists(key)) continue;
                    float old = ClientApi.Settings.Float.Get(key, 70f);
                    viewfinderOldFloatSettings[key] = old;

                    float newFov = old * ViewfinderZoomMultiplier;
                    newFov = Math.Max(30f, Math.Min(110f, newFov));
                    viewfinderTargetFloatSettings[key] = newFov;
                    ClientApi.Settings.Float.Set(key, newFov, true);
                }
            }
        }

        public void EndViewfinderMode()
        {
            if (ClientApi == null) return;

            lock (viewfinderLock)
            {
                if (viewfinderDepth <= 0) return;
                viewfinderDepth--;
                if (viewfinderDepth > 0) return;

                if (runtimeFovSetter != null)
                {
                    SafeSetFov(runtimeFovSetter, runtimeOldFov);
                    runtimeFovGetter = null;
                    runtimeFovSetter = null;
                    runtimeFovMechanism = null;

                    viewfinderOldFloatSettings.Clear();
                    viewfinderTargetFloatSettings.Clear();
                    return;
                }

                foreach (var kvp in viewfinderOldFloatSettings)
                {
                    if (ClientApi.Settings.Float.Exists(kvp.Key))
                    {
                        ClientApi.Settings.Float.Set(kvp.Key, kvp.Value, true);
                    }
                }

                viewfinderOldFloatSettings.Clear();
                viewfinderTargetFloatSettings.Clear();
            }
        }

        private void MaybeShowF4GuiLessTip()
        {
            if (ClientApi == null) return;
            if (f4TipShownThisViewfinder || f4TipShownEver) return;
            if (IsGuiLessModeActive()) return;

            f4TipShownThisViewfinder = true;
            f4TipShownEver = true;
            ClientApi.ShowChatMessage("Wetplate: Tip — press F4 to toggle gui-less mode (hide HUD) while using the viewfinder.");
        }

        private bool IsGuiLessModeActive()
        {
            if (ClientApi == null) return false;

            try
            {
                var t = ClientApi.GetType();
                var p = t.GetProperty("HideGuis") ?? t.GetProperty("HideGUIs") ?? t.GetProperty("HideGui");
                if (p != null && p.PropertyType == typeof(bool))
                {
                    return (bool)(p.GetValue(ClientApi) ?? false);
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        public bool RequestPhotoCaptureFromViewfinder(EntityAgent byEntity, bool silentIfBusy = false)
        {
            if (ClientApi == null || ClientChannel == null || CaptureRenderer == null) return false;

            if (!IsViewfinderActive)
            {
                // Only allow shutter while aiming.
                return false;
            }

            // Prevent "late shutter" after RMB release.
            if (!GetRightMouseDown())
            {
                return false;
            }

            // Shutter gating: You can only take a photo when a SILVERED plate is loaded.
            // You should always be able to zoom, so we gate only capture (not BeginViewfinderMode).
            try
            {
                ItemSlot? camSlot = ClientApi.World.Player?.InventoryManager?.ActiveHotbarSlot;
                ItemStack? camStack = camSlot?.Itemstack;
                if (camStack?.Item is not ItemWetplateCamera)
                {
                    return false;
                }

                string loaded = camStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty) ?? string.Empty;
                if (!loaded.Equals("collodion:silveredplate", StringComparison.OrdinalIgnoreCase))
                {
                    // Always show feedback (but throttle so we don't spam while LMB is held).
                    long nowMs = Environment.TickCount64;
                    if (nowMs - lastShutterGateChatMs > 1000)
                    {
                        lastShutterGateChatMs = nowMs;
                        ClientApi.ShowChatMessage("Wetplate: load a silvered plate to take a photo.");
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }

            // If the player wants an immersive, HUD-free viewfinder, rely on the game's built-in gui-less mode.
            MaybeShowF4GuiLessTip();

            // After taking a shot we want to exit viewfinder and not instantly re-enter until RMB is released.
            suppressViewfinderUntilRmbReleased = true;

            if (ClientChannel == null)
            {
                captureInProgress = false;
                EndViewfinderMode();
                return false;
            }

            bool scheduled = CaptureRenderer.TryScheduleCapture(
                out string fileName,
                onSuccess: (fn) =>
                {
                    captureInProgress = false;
                    // Send to server + play sound after capture completes.
                    ClientChannel.SendPacket(new PhotoTakenPacket() { PhotoId = fn });
                    PhotoSync?.ClientOnPhotoCreated(fn);
                    ClientApi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/woodclick"), byEntity, null, true, 32, 1f);

                    // Capture is already done (we are in the onSuccess callback), so it is safe to exit immediately.
                    EndViewfinderMode();
                },
                onError: (ex) =>
                {
                    captureInProgress = false;
                    ClientApi.Logger.Error("Wetplate HUD-less capture failed: " + ex);
                    ClientApi.ShowChatMessage("Wetplate: capture failed (see log). Falling back may be needed.");

                    // Still exit viewfinder (error means no screenshot was taken).
                    EndViewfinderMode();
                }
            );

            if (!scheduled)
            {
                if (!silentIfBusy)
                {
                    ClientApi.ShowChatMessage("Wetplate: capture already in progress...");
                }
                return false;
            }

            captureInProgress = true;

            return true;
        }

        private void OnClientViewfinderTick(float dt)
        {
            if (ClientApi == null) return;

            ItemSlot? activeSlot = ClientApi.World.Player?.InventoryManager?.ActiveHotbarSlot;
            bool holdingCamera = activeSlot?.Itemstack?.Item is ItemWetplateCamera;

            bool rightDown = GetRightMouseDown();
            bool leftDown = GetLeftMouseDown();
            bool leftPressed = leftDown && !lastLmbDown;
            lastLmbDown = leftDown;

            bool rightPressed = rightDown && !lastRmbDown;
            lastRmbDown = rightDown;

            // Shift+RMB is reserved for loading a plate into the camera (no zoom/viewfinder).
            bool shiftDown = ClientApi.World.Player?.Entity?.Controls?.ShiftKey == true || ClientApi.World.Player?.Entity?.Controls?.Sneak == true;
            if (holdingCamera && shiftDown && rightDown && !IsViewfinderActive)
            {
                // Prevent viewfinder from starting if the player releases shift while still holding RMB.
                suppressViewfinderUntilRmbReleased = true;

                if (rightPressed && ClientChannel != null)
                {
                    ItemSlot? offhand = ClientApi.World.Player?.InventoryManager?.OffhandHotbarSlot;
                    ItemStack? offstack = offhand?.Itemstack;

                    ItemStack? camstack = activeSlot?.Itemstack;
                    string loadedCode = camstack?.Attributes?.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty) ?? string.Empty;
                    bool cameraLoaded = !string.IsNullOrEmpty(loadedCode);

                    // Unload: only when offhand is empty.
                    if (cameraLoaded)
                    {
                        if (offhand != null && offhand.Empty)
                        {
                            ClientChannel.SendPacket(new CameraLoadPlatePacket { Load = false });
                        }
                    }
                    else
                    {
                        // Load: accept either silvered or exposed plate in offhand.
                        if (offstack?.Collectible?.Code != null
                            && offstack.Collectible.Code.Domain == "collodion"
                            && (offstack.Collectible.Code.Path == "silveredplate" || offstack.Collectible.Code.Path == "exposedplate"))
                        {
                            ClientChannel.SendPacket(new CameraLoadPlatePacket { Load = true });
                        }
                    }
                }

                return;
            }

            if (IsViewfinderActive)
            {
                EnsureViewfinderZoomApplied();
            }

            if (!holdingCamera)
            {
                if (!captureInProgress)
                {
                    suppressViewfinderUntilRmbReleased = false;
                    rmbUpSeconds = 0f;
                    if (IsViewfinderActive) EndViewfinderMode();
                }
                return;
            }

            if (!rightDown)
            {
                rmbUpSeconds += dt;
                if (!captureInProgress && rmbUpSeconds > RmbReleaseGraceSeconds)
                {
                    suppressViewfinderUntilRmbReleased = false;
                    if (IsViewfinderActive) EndViewfinderMode();
                }
                return;
            }

            rmbUpSeconds = 0f;

            // RMB is down and camera is held.
            if (suppressViewfinderUntilRmbReleased) return;
            if (!IsViewfinderActive) BeginViewfinderMode();

            // Shutter: LMB rising edge while in viewfinder.
            if (!captureInProgress && IsViewfinderActive && leftPressed)
            {
                var playerEnt = ClientApi.World.Player?.Entity;
                if (playerEnt != null)
                {
                    RequestPhotoCaptureFromViewfinder(playerEnt, silentIfBusy: true);
                }
            }
        }

        private void EnsureViewfinderZoomApplied()
        {
            if (ClientApi == null) return;

            lock (viewfinderLock)
            {
                if (viewfinderDepth <= 0) return;

                if (!zoomMechanismTipShownThisViewfinder)
                {
                    zoomMechanismTipShownThisViewfinder = true;
                    if (ClientConfig?.ShowDebugLogs == true)
                    {
                        string? mech = GetZoomMechanismForTip();
                        if (!string.IsNullOrEmpty(mech))
                        {
                            ClientApi.ShowChatMessage($"Wetplate: viewfinder zoom via {mech}");
                        }
                        else
                        {
                            ClientApi.ShowChatMessage("Wetplate: viewfinder zoom via Settings.Float (fallback)");
                        }
                    }
                }

                if (runtimeFovSetter != null && runtimeFovGetter != null)
                {
                    float current = SafeGetFov(runtimeFovGetter, fallback: runtimeTargetFov);
                    if (Math.Abs(current - runtimeTargetFov) > 0.001f)
                    {
                        SafeSetFov(runtimeFovSetter, runtimeTargetFov);
                    }
                    return;
                }

                foreach (var kvp in viewfinderTargetFloatSettings)
                {
                    if (!ClientApi.Settings.Float.Exists(kvp.Key)) continue;
                    float current = ClientApi.Settings.Float.Get(kvp.Key, kvp.Value);
                    if (Math.Abs(current - kvp.Value) > 0.001f)
                    {
                        ClientApi.Settings.Float.Set(kvp.Key, kvp.Value, true);
                    }
                }
            }
        }

    }
}
