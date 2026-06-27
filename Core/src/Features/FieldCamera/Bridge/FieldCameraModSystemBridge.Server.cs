using Photocore.CameraCapture.Contracts;
using Vintagestory.API.Server;

namespace Photocore.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        internal void ConfigureServerFieldCameraStartup(ICoreServerAPI api)
        {
            ServerChannel = api.Network.GetChannel("photocore");
            ServerChannel.SetMessageHandler<PhotoTakenPacket>(OnPhotoTakenReceived);
            ServerChannel.SetMessageHandler<CameraLoadPlatePacket>(OnCameraLoadPlateReceived);
            ServerChannel.SetMessageHandler<CameraTripodPacket>(OnCameraTripodReceived);
            ServerChannel.SetMessageHandler<ExposureStatePacket>(OnExposureStateReceived);
            ServerChannel.SetMessageHandler<CameraMountRequestPacket>(OnCameraMountRequestReceived);
            ServerChannel.SetMessageHandler<SealAndInsertIntoTrayPacket>(OnSealAndInsertTrayReceived);
            ServerChannel.SetMessageHandler<CameraRestPacket>(OnCameraRestReceived);

            api.Event.PlayerDisconnect += OnPlayerDisconnect;
        }

        internal void DisposeServerFieldCamera(ICoreServerAPI api)
        {
            api.Event.PlayerDisconnect -= OnPlayerDisconnect;
        }
    }
}
