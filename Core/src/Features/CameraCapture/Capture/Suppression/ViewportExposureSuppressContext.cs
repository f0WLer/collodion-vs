using Vintagestory.API.MathTools;

namespace Photocore.CameraCapture
{
    // Consulted by EntityPlayerShapeRenderer Harmony patches; main-thread only.
    internal static class ViewportExposureSuppressContext
    {
        // Set during VirtualCamera renders; read by block-entity renderers to skip the active mounted camera.
        internal static bool IsVirtualRender;
        // Only the camera the player is shooting through is hidden — others stay visible so they appear in the exposure.
        internal static BlockPos? ActiveMountedCameraPos;
        internal static bool ViewfinderActive;
        internal static bool ExposureCapturing;
        internal static bool SuppressLocalPlayer => ViewfinderActive || ExposureCapturing;
    }
}
