using HarmonyLib;
using Vintagestory.API.Client;

using Photocore.Configuration;
using Photocore.Exposure;

namespace Photocore.CameraCapture
{
    internal sealed partial class CameraCaptureModSystemBridge
    {
        private HandheldPreviewRenderer? _handheldPreviewRenderer;
        internal VirtualCameraPreviewRenderer? _virtualCameraPreviewRenderer;
        internal VirtualExposureRenderer? _virtualExposureRenderer;

        private Harmony? _selfPortraitHarmony;

        internal void ConfigureClientCameraCaptureStartup(ICoreClientAPI api)
        {
            ConfigureClientCameraCaptureChannelHandlers();
            ConfigureClientCameraCaptureRenderers(api);

            // Patch EntityPlayerShapeRenderer to fix the self-portrait model-matrix offset.
            // EntityPlayerShapeRenderer lives in VSEssentials.dll which is a game content mod,
            // not directly referenceable at compile time — use AccessTools.TypeByName.
            // If the type is unavailable (extreme edge case), self-portrait silently degrades.
            _selfPortraitHarmony = new Harmony("photocore.selfportrait");
            Type? playerShapeRendererType =
                AccessTools.TypeByName("Vintagestory.GameContent.EntityPlayerShapeRenderer");
            if (playerShapeRendererType != null)
            {
                _selfPortraitHarmony.Patch(
                    AccessTools.Method(playerShapeRendererType, "loadModelMatrixForPlayer"),
                    prefix: new HarmonyMethod(typeof(EntityPlayerSelfPortraitPatch), "Prefix"),
                    postfix: new HarmonyMethod(typeof(EntityPlayerSelfPortraitPatch), "Postfix"));

                _selfPortraitHarmony.Patch(
                    AccessTools.Method(playerShapeRendererType, "DoRender3DOpaque"),
                    prefix: new HarmonyMethod(typeof(EntityPlayerSelfPortraitPatch), "LocalPlayerBodyRenderSuppressPrefix"));
            }

            // SystemSelectedBlockOutline lives in VintagestoryLib, accessible by type name.
            Type? blockOutlineType =
                AccessTools.TypeByName("Vintagestory.Client.NoObf.SystemSelectedBlockOutline");
            if (blockOutlineType != null)
            {
                _selfPortraitHarmony.Patch(
                    AccessTools.Method(blockOutlineType, "OnRenderFrame3DPost"),
                    prefix: new HarmonyMethod(typeof(SelectedBlockOutlinePatch), "SuppressPrefix"));
            }
        }

        private void ConfigureClientCameraCaptureChannelHandlers()
        {
            if (ClientChannel == null) return;

            _owner.PhotoSyncModSystemBridge.ConfigureClientPhotoSyncTransferChannelHandlers();
            _owner.AdminToolingBridge.ConfigureClientDevelopPermissionChannelHandler();
            ClientChannel.SetMessageHandler<ServerConfigOverridePacket>(OnServerConfigOverrideReceived);
        }

        private void ConfigureClientCameraCaptureRenderers(ICoreClientAPI api)
        {
            _virtualCameraPreviewRenderer = new VirtualCameraPreviewRenderer(api);
            api.Event.RegisterRenderer(_virtualCameraPreviewRenderer, EnumRenderStage.Before, "photocore-virtualcamera-preview");

            _virtualExposureRenderer = new VirtualExposureRenderer(api)
            {
                ExposurePreviewSink = _virtualCameraPreviewRenderer
            };
            _virtualCameraPreviewRenderer.ExposureRenderer = _virtualExposureRenderer;
            api.Event.RegisterRenderer(_virtualExposureRenderer, EnumRenderStage.Before, "photocore-virtualexposure");

            _handheldPreviewRenderer = new HandheldPreviewRenderer(api, _virtualCameraPreviewRenderer);
            api.Event.RegisterRenderer(_handheldPreviewRenderer, EnumRenderStage.Ortho, "photocore-viewfinder-preview");

            // Some load orders/world joins invoke StartClientSide before the channel reports connected.
            // Defer send until connected so startup never aborts.
            TrySendServerConfigOverrideRequest(api);
        }

        private long? _clientConfigOverrideRetryTickListenerId;

        // Cached so a later local reload (e.g. a ConfigLib edit) can re-pin these to the server's value
        // in multiplayer instead of letting a joining player's own local file value leak back in —
        // see ConfigLibIntegration.ReapplyConfig.
        internal int? ServerPhotoCaptureMaxDimensionOverride { get; private set; }
        internal bool? ServerApplyFinishingEffectsOverride { get; private set; }
        internal int? ServerPhotoSeenPingIntervalSecondsOverride { get; private set; }

        private void OnServerConfigOverrideReceived(ServerConfigOverridePacket packet)
        {
            if (packet == null) return;

            Config = ConfigLifecycle.EnsureNormalized(Config);
            Config.Viewfinder.PhotoCaptureMaxDimension = packet.MaxDimension;
            Config.Viewfinder.ApplyFinishingEffects = packet.ApplyFinishingEffects;
            Config.Viewfinder.ClampInPlace();

            Config.PhotoSync.PhotoSeenPingIntervalSeconds = packet.PhotoSeenPingIntervalSeconds;
            Config.PhotoSync.ClampInPlace();

            ServerPhotoCaptureMaxDimensionOverride = packet.MaxDimension;
            ServerApplyFinishingEffectsOverride = packet.ApplyFinishingEffects;
            ServerPhotoSeenPingIntervalSecondsOverride = packet.PhotoSeenPingIntervalSeconds;

            // Singleplayer/hosting: client and server share one process and one chemistry-profiles.json
            // already, so there's no divergence to guard against and the tuning GUI should stay saveable.
            if (ClientApi?.IsSinglePlayer == false)
                ChemistryProfileRegistry.ApplyServerProfiles(packet.ChemistryProfilesJson, ClientApi?.Logger);
        }

        private void TrySendServerConfigOverrideRequest(ICoreClientAPI capi)
        {
            if (TrySendServerConfigOverrideRequestNow())
            {
                UnregisterClientConfigOverrideRetry(capi, "unregister immediate config override retry listener");
                return;
            }

            EnsureClientConfigOverrideRetry(capi);
        }

        private bool TrySendServerConfigOverrideRequestNow()
        {
            if (ClientChannel == null || !ClientChannel.Connected) return false;

            try
            {
                ClientChannel.SendPacket(new ServerConfigOverrideRequestPacket());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureClientConfigOverrideRetry(ICoreClientAPI capi)
        {
            if (_clientConfigOverrideRetryTickListenerId.HasValue && _clientConfigOverrideRetryTickListenerId.Value > 0) return;

            _clientConfigOverrideRetryTickListenerId = capi.Event.RegisterGameTickListener(_ =>
            {
                if (!TrySendServerConfigOverrideRequestNow()) return;

                UnregisterClientConfigOverrideRetry(capi, "unregister delayed config override retry listener");
            }, 200, 200);
        }

        private void UnregisterClientConfigOverrideRetry(ICoreClientAPI capi, string operation)
        {
            if (!_clientConfigOverrideRetryTickListenerId.HasValue || _clientConfigOverrideRetryTickListenerId.Value <= 0) return;

            long id = _clientConfigOverrideRetryTickListenerId.Value;
            BestEffort.Try(BestEffortLogger, operation, () => capi.Event.UnregisterGameTickListener(id));
            _clientConfigOverrideRetryTickListenerId = null;
        }
        internal void DisposeClientCameraCaptureRenderers()
        {
            if (ClientApi == null) return;

            BestEffort.Try(BestEffortLogger, "dispose viewfinder exposure registry", () =>
            {
                ViewfinderExposureRegistry.Clear();
                ActiveAccumulator = null;
                ActiveExposureId = string.Empty;
            });

            if (_handheldPreviewRenderer != null)
            {
                BestEffort.Try(BestEffortLogger, "unregister debug preview renderer", () => ClientApi.Event.UnregisterRenderer(_handheldPreviewRenderer, EnumRenderStage.Ortho));
                BestEffort.Try(BestEffortLogger, "dispose debug preview renderer", () => _handheldPreviewRenderer.Dispose());
            }

            if (_virtualCameraPreviewRenderer != null)
            {
                BestEffort.Try(BestEffortLogger, "unregister virtual camera preview renderer", () => ClientApi.Event.UnregisterRenderer(_virtualCameraPreviewRenderer, EnumRenderStage.Before));
                BestEffort.Try(BestEffortLogger, "dispose virtual camera preview renderer", () => _virtualCameraPreviewRenderer.Dispose());
            }

            if (_virtualExposureRenderer != null)
            {
                BestEffort.Try(BestEffortLogger, "unregister virtual exposure renderer", () => ClientApi.Event.UnregisterRenderer(_virtualExposureRenderer, EnumRenderStage.Before));
                BestEffort.Try(BestEffortLogger, "dispose virtual exposure renderer", () => _virtualExposureRenderer.Dispose());
            }

            // Remove the self-portrait Harmony patch so it doesn't linger across hot-reloads or mod unloads.
            BestEffort.Try(BestEffortLogger, "unpatch self-portrait harmony", () =>
            {
                _selfPortraitHarmony?.UnpatchAll("photocore.selfportrait");
                _selfPortraitHarmony = null;
            });
        }

        internal void DisposeClientCameraCaptureTickListeners()
        {
            if (ClientApi == null) return;
            UnregisterClientConfigOverrideRetry(ClientApi, "unregister config override retry tick listener");
        }

        internal void ClearClientCameraCaptureRuntimeReferences()
        {
            ActiveAccumulator = null;
            ActiveExposureId = string.Empty;
            _handheldPreviewRenderer = null;
            _virtualCameraPreviewRenderer = null;
            _virtualExposureRenderer = null;
        }
    }
}
