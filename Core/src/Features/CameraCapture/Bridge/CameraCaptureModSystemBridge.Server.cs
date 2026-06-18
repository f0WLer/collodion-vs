using Collodion.AdminTooling;
using Collodion.CameraCapture.Contracts;
using Collodion.CameraCapture.Integration;

using Vintagestory.API.Server;

namespace Collodion.CameraCapture
{
    internal sealed partial class CameraCaptureModSystemBridge
    {
        // Composes full server-side camera-capture startup so ModSystem root stays declarative.
        internal void ConfigureServerCameraCaptureStartup(ICoreServerAPI api)
        {
            ConfigureServerCameraCaptureCore(api);
            ConfigureServerCameraCaptureSyncHandlers();
            BroadcastServerCameraCaptureConfig(api);
        }

        private void ConfigureServerCameraCaptureCore(ICoreServerAPI api)
        {
            ServerChannel = api.Network.GetChannel("collodion");
            _owner.PhotoSyncModSystemBridge.ConfigureServerPhotoSyncRuntime(api);
        }

        private void ConfigureServerCameraCaptureSyncHandlers()
        {
            if (ServerChannel == null) return;

            _owner.PhotoSyncModSystemBridge.ConfigureServerPhotoSyncTransferChannelHandlers();
            _owner.PhotoSyncModSystemBridge.ConfigureServerPhotoSeenChannelHandler();
            CameraCaptureChannelRegistration.ConfigureServerSyncHandlers(
                ServerChannel,
                (player, p) => OnPhotoCaptureConfigRequested(player));
        }
        private void BroadcastServerCameraCaptureConfig(ICoreServerAPI api)
        {
            // Send once on startup for currently connected players (mainly relevant on hot-reload).
            foreach (IServerPlayer player in api.World.AllOnlinePlayers)
            {
                OnPhotoCaptureConfigRequested(player);
            }
        }

        private void OnPhotoCaptureConfigRequested(IServerPlayer? player)
        {
            if (player == null || ServerChannel == null) return;

            int maxDimension = GetServerPhotoCaptureMaxDimension();
            ServerChannel.SendPacket(new PhotoCaptureConfigPacket
            {
                MaxDimension = maxDimension
            }, player);
        }

        private int GetServerPhotoCaptureMaxDimension()
        {
            Config = ConfigLifecycle.EnsureNormalized(Config);
            Config.Viewfinder.ClampInPlace();
            return Config.Viewfinder.PhotoCaptureMaxDimension;
        }
    }
}
