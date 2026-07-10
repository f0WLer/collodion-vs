using Vintagestory.API.Server;

using Photocore.Configuration;
using Photocore.Exposure;

namespace Photocore.CameraCapture
{
    internal sealed partial class CameraCaptureModSystemBridge
    {
        // Composes full server-side camera-capture startup so ModSystem root stays declarative.
        internal void ConfigureServerCameraCaptureStartup(ICoreServerAPI api)
        {
            ConfigureServerCameraCaptureCore(api);
            ConfigureServerCameraCaptureSyncHandlers();
            BroadcastServerConfigOverride(api);
        }

        private void ConfigureServerCameraCaptureCore(ICoreServerAPI api)
        {
            ServerChannel = api.Network.GetChannel("photocore");
            _owner.PhotoSyncModSystemBridge.ConfigureServerPhotoSyncRuntime(api);
        }

        private void ConfigureServerCameraCaptureSyncHandlers()
        {
            if (ServerChannel == null) return;

            _owner.PhotoSyncModSystemBridge.ConfigureServerPhotoSyncTransferChannelHandlers();
            _owner.PhotoSyncModSystemBridge.ConfigureServerPhotoSeenChannelHandler();
            CameraCaptureChannelRegistration.ConfigureServerSyncHandlers(
                ServerChannel,
                (player, p) => OnServerConfigOverrideRequested(player));
        }
        // Pushes the current authoritative values to every connected player. Run at startup (mainly
        // relevant on hot-reload), and again whenever the server's config is reloaded at runtime.
        // Clients otherwise receive this exactly once, in reply to their own join-time request, so a
        // mid-session change would leave everyone already connected sealing photos with the values from
        // when they joined — the cross-player divergence this override exists to prevent.
        internal void BroadcastServerConfigOverride(ICoreServerAPI api)
        {
            foreach (IServerPlayer player in api.World.AllOnlinePlayers)
            {
                OnServerConfigOverrideRequested(player);
            }
        }

        private void OnServerConfigOverrideRequested(IServerPlayer? player)
        {
            if (player == null || ServerChannel == null) return;

            Config = ConfigLifecycle.EnsureNormalized(Config);
            Config.Viewfinder.ClampInPlace();
            ServerChannel.SendPacket(new ServerConfigOverridePacket
            {
                MaxDimension = Config.Viewfinder.PhotoCaptureMaxDimension,
                ApplyFinishingEffects = Config.Viewfinder.ApplyFinishingEffects,
                PhotoSeenPingIntervalSeconds = Config.PhotoSync.PhotoSeenPingIntervalSeconds,
                ChemistryProfilesJson = ChemistryProfileRegistry.Instance.SerializeCurrent()
            }, player);
        }
    }
}
