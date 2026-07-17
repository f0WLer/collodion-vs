using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace Photocore.CameraCapture
{
    internal sealed partial class CameraCaptureModSystemBridge
    {
        private bool _f4TipShownThisViewfinder;
        private bool _f4TipShownEver;

        private static readonly AssetLocation _viewfinderEnterSound = new AssetLocation("photocore", "sounds/cap-remove");

        private readonly object _viewfinderLock = new object();
        private int _viewfinderDepth;
        private float? _viewfinderSavedFov;
        internal float _viewfinderTargetFov;

        // Per-session zoom value (radians). 0 = not yet set; initialised from ZoomMultiplier on first viewfinder entry.
        private float _viewfinderZoomFovRad;

        private const float ZoomFovMinRad = 5f  * MathF.PI / 180f;   // 5°
        private const float ZoomFovMaxRad = 90f * MathF.PI / 180f;   // 90°
        internal const float ZoomFovStepRad = 5f * MathF.PI / 180f;  // 5° per scroll notch / key press

        private bool _zoomMechanismTipShownThisViewfinder;

        public bool IsViewfinderActive
        {
            get
            {
                lock (_viewfinderLock) return _viewfinderDepth > 0;
            }
        }

        // Kept alive across RMB releases so a paused exposure can be resumed.
        internal IGameplayExposureAccumulator? ActiveAccumulator { get; set; }

        // Primed viewport accumulator created at viewfinder entry so the PBO ring is warm before
        // shutter press.  Consumed (or disposed) in TryToggleViewfinderExposure / EndViewfinderMode.
        internal ViewportExposureAccumulator? _primedViewportAccumulator;

        // Stable identifier for the active or most recently paused exposure session.
        // Used client-side so manual sealing can evict the matching registry entry deterministically.
        internal string ActiveExposureId { get; set; } = string.Empty;

        internal bool IsExposureCapturing => ActiveAccumulator?.State == ExposureState.Capturing;

        private float ViewfinderZoomMultiplierCfg => Config?.Viewfinder?.ZoomMultiplier ?? 0.65f;

        public void BeginViewfinderMode()
        {
            if (ClientApi == null) return;

            lock (_viewfinderLock)
            {
                _viewfinderDepth++;
                if (_viewfinderDepth > 1) return;

                _viewfinderSavedFov = null;
                _zoomMechanismTipShownThisViewfinder = false;

                _f4TipShownThisViewfinder = false;
                MaybeShowF4GuiLessTip();

                IClientWorldAccessor? world = ClientApi.World;
                EntityAgent? playerEnt = world?.Player?.Entity;
                if (world != null && playerEnt != null)
                {
                    AudioUtils.FireAndForgetEntitySound(world, _viewfinderEnterSound, playerEnt, AudioUtils.NextRandomPitch(world));
                }

                ApplyZoomedFov();

                // Prime the async PBO readback ring if this is a fresh viewport (not resuming or capturing).
                // By priming now, the ring has RingSize async ReadPixels in-flight before the shutter opens,
                // so the first capturing tick maps a real frame immediately — no synchronous stall.
                if (_primedViewportAccumulator == null && ClientApi != null && ActiveAccumulator == null)
                {
                    _primedViewportAccumulator = new ViewportExposureAccumulator(ClientApi);
                    _primedViewportAccumulator.Prime();
                }

                ViewportExposureSuppressContext.ViewfinderActive = true;
            }
        }

        private void ApplyZoomedFov()
        {
            if (ClientApi?.World is not ClientMain client || client.MainCamera == null) return;

            float current = client.MainCamera.Fov;
            if (_viewfinderSavedFov == null) _viewfinderSavedFov = current;

            if (_viewfinderZoomFovRad == 0f)
            {
                float baseFov = _viewfinderSavedFov.Value;
                float initial = baseFov * ViewfinderZoomMultiplierCfg;
                _viewfinderZoomFovRad = Math.Max(ZoomFovMinRad, Math.Min(ZoomFovMaxRad, initial));
            }

            client.MainCamera.Fov = _viewfinderZoomFovRad;
            _viewfinderTargetFov = _viewfinderZoomFovRad;

            ClientApi.Render?.Reset3DProjection();
        }

        internal void AdjustViewfinderZoom(float deltaRad)
        {
            lock (_viewfinderLock)
            {
                if (_viewfinderDepth == 0) return;

                float next = Math.Max(ZoomFovMinRad, Math.Min(ZoomFovMaxRad, _viewfinderZoomFovRad + deltaRad));
                if (next == _viewfinderZoomFovRad) return;

                _viewfinderZoomFovRad = next;

                if (ClientApi?.World is ClientMain client && client.MainCamera != null)
                {
                    client.MainCamera.Fov = _viewfinderZoomFovRad;
                    _viewfinderTargetFov  = _viewfinderZoomFovRad;
                    ClientApi.Render?.Reset3DProjection();
                }
            }
        }

        public void EndViewfinderMode()
        {
            if (ClientApi == null) return;

            lock (_viewfinderLock)
            {
                if (_viewfinderDepth <= 0) return;
                _viewfinderDepth--;
                if (_viewfinderDepth > 0) return;

                if (_viewfinderSavedFov is float saved && ClientApi.World is ClientMain client && client.MainCamera != null)
                {
                    client.MainCamera.Fov = saved;
                    ClientApi.Render?.Reset3DProjection();
                }

                _viewfinderSavedFov = null;
                _viewfinderTargetFov = 0f;

                _primedViewportAccumulator?.Dispose();
                _primedViewportAccumulator = null;

                ViewportExposureSuppressContext.ViewfinderActive = false;
            }
        }

        internal void MaybeShowF4GuiLessTip()
        {
            if (ClientApi == null) return;
            if (_f4TipShownThisViewfinder || _f4TipShownEver) return;
            if (IsGuiLessModeActive()) return;

            _f4TipShownThisViewfinder = true;
            _f4TipShownEver = true;
            ClientApi.ShowChatMessage(Lang.Get("photocore:msg-tip-f4-guiless"));
        }

        private bool IsGuiLessModeActive() => ClientApi?.HideGuis ?? false;

        // Reapplies the zoomed FOV if the engine reset MainCamera.Fov (e.g. user changed the FOV slider).
        internal void EnsureViewfinderZoomApplied()
        {
            if (ClientApi == null) return;

            lock (_viewfinderLock)
            {
                if (_viewfinderDepth <= 0) return;

                if (!_zoomMechanismTipShownThisViewfinder)
                {
                    _zoomMechanismTipShownThisViewfinder = true;
                    if (ClientConfig?.ShowDebugLogs == true)
                    {
                        ClientApi.ShowChatMessage("photocore: viewfinder zoom via MainCamera.Fov");
                    }
                }

                if (ClientApi.World is not ClientMain client || client.MainCamera == null) return;
                if (Math.Abs(client.MainCamera.Fov - _viewfinderTargetFov) > 0.001f)
                {
                    ApplyZoomedFov();
                }
            }
        }
    }
}
