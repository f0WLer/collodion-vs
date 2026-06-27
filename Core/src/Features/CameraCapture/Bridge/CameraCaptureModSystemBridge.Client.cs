using HarmonyLib;
using Vintagestory.API.Client;

using Photocore.Configuration;
using Photocore.CameraCapture.Contracts;

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
            ClientChannel.SetMessageHandler<PhotoCaptureConfigPacket>(OnPhotoCaptureConfigReceived);
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
            TrySendPhotoCaptureConfigRequest(api);
        }

        private long? _clientCaptureConfigRetryTickListenerId;

        private void OnPhotoCaptureConfigReceived(PhotoCaptureConfigPacket packet)
        {
            if (packet == null) return;

            Config = ConfigLifecycle.EnsureNormalized(Config);
            Config.Viewfinder.PhotoCaptureMaxDimension = packet.MaxDimension;
            Config.Viewfinder.ClampInPlace();
        }

        private void TrySendPhotoCaptureConfigRequest(ICoreClientAPI capi)
        {
            if (TrySendPhotoCaptureConfigRequestNow())
            {
                UnregisterClientCaptureConfigRetry(capi, "unregister immediate capture config retry listener");
                return;
            }

            EnsureClientCaptureConfigRetry(capi);
        }

        private bool TrySendPhotoCaptureConfigRequestNow()
        {
            if (ClientChannel == null || !ClientChannel.Connected) return false;

            try
            {
                ClientChannel.SendPacket(new PhotoCaptureConfigRequestPacket());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureClientCaptureConfigRetry(ICoreClientAPI capi)
        {
            if (_clientCaptureConfigRetryTickListenerId.HasValue && _clientCaptureConfigRetryTickListenerId.Value > 0) return;

            _clientCaptureConfigRetryTickListenerId = capi.Event.RegisterGameTickListener(_ =>
            {
                if (!TrySendPhotoCaptureConfigRequestNow()) return;

                UnregisterClientCaptureConfigRetry(capi, "unregister delayed capture config retry listener");
            }, 200, 200);
        }

        private void UnregisterClientCaptureConfigRetry(ICoreClientAPI capi, string operation)
        {
            if (!_clientCaptureConfigRetryTickListenerId.HasValue || _clientCaptureConfigRetryTickListenerId.Value <= 0) return;

            long id = _clientCaptureConfigRetryTickListenerId.Value;
            BestEffort.Try(BestEffortLogger, operation, () => capi.Event.UnregisterGameTickListener(id));
            _clientCaptureConfigRetryTickListenerId = null;
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
            UnregisterClientCaptureConfigRetry(ClientApi, "unregister capture config retry tick listener");
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
