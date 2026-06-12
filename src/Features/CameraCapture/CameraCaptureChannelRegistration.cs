using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Collodion.CameraCapture.Contracts;

namespace Collodion.CameraCapture.Integration
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

        internal static void ConfigureClientHandlers(
            IClientNetworkChannel channel,
            NetworkServerMessageHandler<PhotoCaptureConfigPacket> onPhotoCaptureConfigReceived,
            NetworkServerMessageHandler<MountedCameraControlPacket> onMountedCameraControlReceived)
        {
            if (channel == null || onPhotoCaptureConfigReceived == null) return;

            channel.SetMessageHandler<PhotoCaptureConfigPacket>(onPhotoCaptureConfigReceived);

            if (onMountedCameraControlReceived != null)
                channel.SetMessageHandler<MountedCameraControlPacket>(onMountedCameraControlReceived);
        }

        internal static void ConfigureServerCoreHandlers(
            IServerNetworkChannel channel,
            NetworkClientMessageHandler<PhotoTakenPacket> onPhotoTakenReceived,
            NetworkClientMessageHandler<CameraLoadPlatePacket> onCameraLoadPlateReceived,
            NetworkClientMessageHandler<CameraTripodPacket> onCameraTripodReceived,
            NetworkClientMessageHandler<ExposureStatePacket> onExposureStateReceived,
            NetworkClientMessageHandler<CameraMountRequestPacket> onCameraMountRequestReceived)
        {
            if (channel == null) return;

            if (onPhotoTakenReceived != null)
                channel.SetMessageHandler<PhotoTakenPacket>(onPhotoTakenReceived);

            if (onCameraLoadPlateReceived != null)
                channel.SetMessageHandler<CameraLoadPlatePacket>(onCameraLoadPlateReceived);

            if (onCameraTripodReceived != null)
                channel.SetMessageHandler<CameraTripodPacket>(onCameraTripodReceived);

            if (onExposureStateReceived != null)
                channel.SetMessageHandler<ExposureStatePacket>(onExposureStateReceived);

            if (onCameraMountRequestReceived != null)
                channel.SetMessageHandler<CameraMountRequestPacket>(onCameraMountRequestReceived);
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