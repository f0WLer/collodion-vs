using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace Photocore.CameraCapture
{
    internal static class CameraCaptureChannelRegistration
    {
        // Preserves wire-order invariants — do not reorder.
        internal static INetworkChannel RegisterCameraCaptureMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(MountedCameraControlPacket))
                .RegisterMessageType(typeof(CameraLoadPlatePacket))
                .RegisterMessageType(typeof(CameraTripodPacket))
                .RegisterMessageType(typeof(ExposureStatePacket))
                .RegisterMessageType(typeof(CameraMountRequestPacket))
                .RegisterMessageType(typeof(SealAndInsertIntoTrayPacket))
                .RegisterMessageType(typeof(CameraRestPacket));
        }

        // Registers server config override packet DTOs after sync packet DTOs to preserve existing wire order.
        internal static INetworkChannel RegisterServerConfigOverrideMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(ServerConfigOverrideRequestPacket))
                .RegisterMessageType(typeof(ServerConfigOverridePacket));
        }

        internal static void ConfigureServerSyncHandlers(
            IServerNetworkChannel channel,
            NetworkClientMessageHandler<ServerConfigOverrideRequestPacket> onServerConfigOverrideRequested)
        {
            if (channel == null || onServerConfigOverrideRequested == null) return;

            channel.SetMessageHandler<ServerConfigOverrideRequestPacket>(onServerConfigOverrideRequested);
        }
    }
}
