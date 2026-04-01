using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Collodion
{
    public partial class CollodionModSystem
    {
    private long clientPhotoSeenLastPruneMs;
    private long? clientCaptureConfigRetryTickListenerId;

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api;
            ClientInstance = this;
            ClientChannel = api.Network.GetChannel("collodion");
            PhotoSync = new WetplatePhotoSync(this);

            Config = LoadOrCreateConfig(api);
            ClientConfig = Config.Client;

            ClientChannel
                .SetMessageHandler<PhotoBlobChunkPacket>((p) => PhotoSync?.ClientHandleChunk(p))
                .SetMessageHandler<PhotoBlobAckPacket>((p) => PhotoSync?.ClientHandleAck(p))
                .SetMessageHandler<PhotoCaptureConfigPacket>(OnPhotoCaptureConfigReceived);

            api.Input.RegisterHotKey("collodion-toggle-camerasling", "Toggle camera in sling", GlKeys.R, HotkeyType.CharacterControls);
            api.Input.SetHotKeyHandler("collodion-toggle-camerasling", OnCameraSlingHotkey);

            try
            {
                var asm = typeof(CollodionModSystem).Assembly;
                string ver = asm.GetName().Version?.ToString() ?? "<nover>";
                string loc = asm.Location;
                string stamp = "<unknown>";
                try
                {
                    if (!string.IsNullOrEmpty(loc))
                    {
                        stamp = System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
                catch { /* intentional: best-effort non-critical path */ }

                if (ClientConfig?.ShowDebugLogs == true)
                {
                    ClientApi.ShowChatMessage($"Collodion: loaded mod dll (ver={ver}, build={stamp})");
                }
            }
            catch { /* intentional: best-effort non-critical path */ }

            // Capture screenshots after the 3D scene is blitted to the default framebuffer,
            // but before GUI/HUD is rendered (EnumRenderStage.AfterBlit).
            CaptureRenderer = new PhotoCaptureRenderer(api);
            CaptureRenderer.SetCaptureMaxDimension(Config.Viewfinder.PhotoCaptureMaxDimension);
            api.Event.RegisterRenderer(CaptureRenderer, EnumRenderStage.AfterBlit, "collodion-photocapture");

            DebugPreviewRenderer = new ViewfinderDebugPreviewRenderer(api, CaptureRenderer, () => IsViewfinderActive);
            api.Event.RegisterRenderer(DebugPreviewRenderer, EnumRenderStage.Ortho, "collodion-viewfinder-preview");

            // Ask server for authoritative capture sizing in multiplayer.
            // Some load orders/world joins invoke StartClientSide before the channel reports connected.
            // Defer send until connected so startup never aborts.
            TrySendPhotoCaptureConfigRequest(api);

#pragma warning disable CS0618 // Keep legacy command registration for compatibility
            api.RegisterCommand(
                "collodion",
                "Collodion mod commands",
                ".collodion clearcache | .collodion hud | .collodion preview | .collodion pose | .collodion effects",
                OnWetplateClientCommand
            );
#pragma warning restore CS0618

            // Some client setups don't reliably invoke OnHeldInteractStart for held items
            // (especially when aiming at air). Poll RMB state as a fallback.
            viewfinderTickListenerId = api.Event.RegisterGameTickListener(OnClientViewfinderTick, 20, 0);

            // Dev tray: ensure timed interactions require an RMB release between stages.
            // Clear the latch only when RMB is actually up.
            clientDevTrayLatchTickListenerId = api.Event.RegisterGameTickListener(OnClientDevTrayLatchTick, 20, 0);

            // Patch Set3DProjection so viewfinder can zoom reliably.
            TryEnsureHarmonyProjectionZoomPatch();
            // Note: do NOT also force RenderAPI.Set3DProjection per-frame; that can lead to
            // mismatched projections (e.g., skybox-only zoom). The hook on
            // ClientMain.Set3DProjection affects the actual world projection.
        }

        private void OnPhotoCaptureConfigReceived(PhotoCaptureConfigPacket packet)
        {
            if (packet == null) return;

            Config ??= new CollodionConfig();
            Config.Viewfinder ??= new ViewfinderConfig();
            Config.Viewfinder.PhotoCaptureMaxDimension = packet.MaxDimension;
            Config.Viewfinder.ClampInPlace();

            CaptureRenderer?.SetCaptureMaxDimension(Config.Viewfinder.PhotoCaptureMaxDimension);
        }

        private void TrySendPhotoCaptureConfigRequest(ICoreClientAPI capi)
        {
            if (ClientChannel == null) return;

            if (ClientChannel.Connected)
            {
                try
                {
                    ClientChannel.SendPacket(new PhotoCaptureConfigRequestPacket());
                }
                catch
                {
                    // Retry via tick listener below.
                }

                if (clientCaptureConfigRetryTickListenerId.HasValue && clientCaptureConfigRetryTickListenerId.Value > 0)
                {
                    try { capi.Event.UnregisterGameTickListener(clientCaptureConfigRetryTickListenerId.Value); } catch { /* intentional: best-effort non-critical path */ }
                    clientCaptureConfigRetryTickListenerId = null;
                }

                return;
            }

            if (clientCaptureConfigRetryTickListenerId.HasValue && clientCaptureConfigRetryTickListenerId.Value > 0)
            {
                return;
            }

            clientCaptureConfigRetryTickListenerId = capi.Event.RegisterGameTickListener(_ =>
            {
                if (ClientChannel == null || !ClientChannel.Connected) return;

                try
                {
                    ClientChannel.SendPacket(new PhotoCaptureConfigRequestPacket());
                }
                catch
                {
                    return;
                }

                if (clientCaptureConfigRetryTickListenerId.HasValue && clientCaptureConfigRetryTickListenerId.Value > 0)
                {
                    try { capi.Event.UnregisterGameTickListener(clientCaptureConfigRetryTickListenerId.Value); } catch { /* intentional: best-effort non-critical path */ }
                    clientCaptureConfigRetryTickListenerId = null;
                }
            }, 200, 200);
        }

        private CollodionConfig LoadOrCreateConfig(ICoreClientAPI capi)
        {
            CollodionConfig? cfg = null;
            try
            {
                cfg = capi.LoadModConfig<CollodionConfig>(ConfigFileName);
            }
            catch
            {
                cfg = null;
            }

            if (cfg == null)
            {
                cfg = new CollodionConfig();
                try
                {
                    cfg.ClampInPlace();
                    capi.StoreModConfig(cfg, ConfigFileName);
                }
                catch
                {
                    // ignore
                }
            }

            cfg.ClampInPlace();
            return cfg;
        }

        internal CollodionConfig GetOrLoadClientConfig(ICoreClientAPI capi)
        {
            if (Config == null)
            {
                Config = LoadOrCreateConfig(capi);
            }

            Config.Client ??= new CollodionClientConfig();
            Config.Effects ??= new WetplateEffectsConfig();
            Config.EffectsDeveloped ??= CollodionConfig.CreateDevelopedEffectsDefaults();
            Config.EffectsPresetIndoor ??= new WetplateEffectsConfig();
            Config.EffectsPresetOutdoor ??= new WetplateEffectsConfig();

            Config.ClampInPlace();
            ClientConfig = Config.Client;
            return Config;
        }

        internal void SaveClientConfig(ICoreClientAPI capi)
        {
            if (Config == null) return;
            try
            {
                Config.ClampInPlace();
                capi.StoreModConfig(Config, ConfigFileName);
            }
            catch
            {
                // ignore
            }
        }

        internal void ClientMaybeSendPhotoSeen(string photoId)
        {
            if (ClientApi == null || ClientChannel == null) return;

            int intervalSeconds = ClientConfig?.PhotoSeenPingIntervalSeconds ?? 0;
            if (intervalSeconds <= 0) return;

            photoId = WetplatePhotoSync.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoId)) return;

            long nowMs;
            try
            {
                nowMs = (long)ClientApi.World.ElapsedMilliseconds;
            }
            catch
            {
                return;
            }

            // Keep the dedupe map bounded during long sessions.
            if (nowMs - clientPhotoSeenLastPruneMs >= 30_000)
            {
                clientPhotoSeenLastPruneMs = nowMs;

                long retainMs = Math.Max(300_000L, intervalSeconds * 4000L);
                List<string>? staleKeys = null;
                foreach (KeyValuePair<string, long> kvp in clientLastPhotoSeenPingMs)
                {
                    if (nowMs - kvp.Value <= retainMs) continue;
                    staleKeys ??= new List<string>();
                    staleKeys.Add(kvp.Key);
                }

                if (staleKeys != null)
                {
                    foreach (string key in staleKeys)
                    {
                        clientLastPhotoSeenPingMs.Remove(key);
                    }
                }
            }

            if (clientLastPhotoSeenPingMs.TryGetValue(photoId, out long lastMs))
            {
                if (nowMs - lastMs < intervalSeconds * 1000L) return;
            }

            clientLastPhotoSeenPingMs[photoId] = nowMs;
            ClientChannel.SendPacket(new PhotoSeenPacket { PhotoId = photoId });
        }

        private bool OnCameraSlingHotkey(KeyCombination _)
        {
            if (ClientApi?.World?.Player == null || ClientChannel == null) return false;

            var packet = new CameraSlingTogglePacket();
            try
            {
                object? sel = ClientApi.World.Player.CurrentBlockSelection;
                if (sel != null)
                {
                    object? pos = GetMemberValue(sel, "Position");
                    object? face = GetMemberValue(sel, "Face");
                    if (pos == null || face == null)
                    {
                        ClientChannel.SendPacket(packet);
                        return true;
                    }

                    int x = GetIntMemberValue(pos, "X");
                    int y = GetIntMemberValue(pos, "Y");
                    int z = GetIntMemberValue(pos, "Z");
                    string faceCode = GetMemberValue(face, "Code")?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(faceCode))
                    {
                        packet.TryWallMount = true;
                        packet.TargetX = x;
                        packet.TargetY = y;
                        packet.TargetZ = z;
                        packet.TargetFaceCode = faceCode;
                    }
                }
            }
            catch
            {
                // ignore and send basic toggle packet
            }

            ClientChannel.SendPacket(packet);
            return true;
        }

        private static object? GetMemberValue(object instance, string memberName)
        {
            var type = instance.GetType();

            var prop = type.GetProperty(memberName);
            if (prop != null)
            {
                try { return prop.GetValue(instance); }
                catch { /* intentional: best-effort non-critical path */ }
            }

            var field = type.GetField(memberName);
            if (field != null)
            {
                try { return field.GetValue(instance); }
                catch { /* intentional: best-effort non-critical path */ }
            }

            return null;
        }

        private static int GetIntMemberValue(object instance, string memberName)
        {
            object? value = GetMemberValue(instance, memberName);
            if (value == null) return 0;

            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }
    }
}

