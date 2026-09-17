namespace Photocore.CameraCapture
{
    // CameraCapture startup composition and packet registration wiring.
    // Keeps camera packet registration ownership in the CameraCapture feature root.
    internal sealed partial class CameraCaptureModSystemBridge : ModSystemBridgeBase
    {
        internal CameraCaptureModSystemBridge(PhotocoreModSystem owner) : base(owner)
        {
        }
    }
}
