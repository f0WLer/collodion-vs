using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

using Photocore.CameraCapture;
using Photocore.Plates;

namespace Photocore.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        private long _viewfinderTickListenerId;
        private FieldCameraClientRuntime? _fieldCameraClientRuntime;
        internal FieldCameraClientRuntime CaptureClientRuntime => _fieldCameraClientRuntime ??= new FieldCameraClientRuntime(this);

        internal void ConfigureClientFieldCameraStartup(ICoreClientAPI api)
        {
            ClientChannel?.SetMessageHandler<MountedCameraControlPacket>(OnMountedCameraControlReceived);
            ConfigureClientFieldCameraInputAndProjection(api);
        }

        private void ConfigureClientFieldCameraInputAndProjection(ICoreClientAPI api)
        {
            _viewfinderTickListenerId = api.Event.RegisterGameTickListener(CaptureClientRuntime.OnClientViewfinderTick, 20, 0);
            CaptureClientRuntime.SubscribeMouseEvents(api);
        }

        private void OnMountedCameraControlReceived(MountedCameraControlPacket packet)
        {
            if (packet == null) return;
            CaptureClientRuntime.ApplyMountedExposureControl(packet);
        }

        internal bool RequestMountedPhotoCapture(bool silentIfBusy = false)
        {
            var capi = ClientApi;
            if (capi != null)
            {
                ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(capi);
                if (camStack != null
                    && CameraItemHelper.TryGetLoadedPlateStack(camStack, capi.World, out ItemStack? plate) && plate != null)
                {
                    PlateStage stage = PlateAttributes.GetStage(plate);
                    if (stage is PlateStage.Exposing or PlateStage.ExposurePaused)
                    {
                        string? uid = plate.Attributes.GetString(PlateAttributes.PhotographerUid);
                        if (!string.IsNullOrEmpty(uid)
                            && !string.Equals(uid, capi.World.Player.PlayerUID, StringComparison.Ordinal))
                        {
                            capi.ShowChatMessage(Lang.Get("photocore:msg-plate-other-photographer"));
                            return false;
                        }
                    }
                }
            }
            return CaptureClientRuntime.TryToggleMountedExposure(silentIfBusy);
        }

        internal bool TryToggleViewfinderExposure(EntityAgent byEntity, bool silentIfBusy = false)
        {
            return CaptureClientRuntime.TryToggleViewfinderExposure(byEntity, silentIfBusy);
        }

        internal void DisposeClientFieldCameraTickListeners()
        {
            if (ClientApi == null) return;

            CaptureClientRuntime.UnsubscribeMouseEvents(ClientApi);

            if (_viewfinderTickListenerId > 0)
            {
                BestEffort.Try(BestEffortLogger, "unregister viewfinder tick listener",
                    () => ClientApi.Event.UnregisterGameTickListener(_viewfinderTickListenerId));
                _viewfinderTickListenerId = 0;
            }
        }

        internal void ClearClientFieldCameraRuntimeReferences()
        {
            _fieldCameraClientRuntime = null;
        }
    }
}
