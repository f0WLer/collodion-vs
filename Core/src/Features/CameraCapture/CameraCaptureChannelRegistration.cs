using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Photochemistry.CameraCapture.Contracts;

namespace Photochemistry.CameraCapture.Integration
{
    internal static class CameraCaptureChannelRegistration
    {
        // Preserves wire-order invariants — do not reorder.
        internal static INetworkChannel RegisterCameraCaptureMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(MountedCameraControlPacket))
                .RegisterMessageType(typeof(PhotoTakenPacket))
                .RegisterMessageType(typeof(CameraLoadPlatePacket))
                .RegisterMessageType(typeof(CameraTripodPacket))
                .RegisterMessageType(typeof(ExposureStatePacket))
                .RegisterMessageType(typeof(CameraMountRequestPacket))
                .RegisterMessageType(typeof(SealAndInsertIntoTrayPacket))
                .RegisterMessageType(typeof(CameraRestPacket));
        }

        // Registers CameraCapture config packet DTOs after sync packet DTOs to preserve existing wire order.
        internal static INetworkChannel RegisterCameraCaptureConfigMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(PhotoCaptureConfigRequestPacket))
                .RegisterMessageType(typeof(PhotoCaptureConfigPacket));
        }

        internal static void ConfigureServerSyncHandlers(
            IServerNetworkChannel channel,
            NetworkClientMessageHandler<PhotoCaptureConfigRequestPacket> onPhotoCaptureConfigRequested)
        {
            if (channel == null || onPhotoCaptureConfigRequested == null) return;

            channel.SetMessageHandler<PhotoCaptureConfigRequestPacket>(onPhotoCaptureConfigRequested);
        }
    }
}