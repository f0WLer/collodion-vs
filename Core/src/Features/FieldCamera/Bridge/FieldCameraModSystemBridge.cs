using Photocore.CameraCapture;

namespace Photocore.FieldCamera
{
    // Capture is the seam handle for reaching CameraCapture pipeline members.
    internal sealed partial class FieldCameraModSystemBridge : ModSystemBridgeBase
    {
        internal FieldCameraModSystemBridge(PhotocoreModSystem owner) : base(owner)
        {
        }

        internal CameraCaptureModSystemBridge Capture => _owner.CameraCaptureBridge;
    }
}
