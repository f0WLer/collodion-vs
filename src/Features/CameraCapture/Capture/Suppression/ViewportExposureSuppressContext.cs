using Vintagestory.API.MathTools;

namespace Collodion.CameraCapture
{
    /// <summary>
    /// Shared state consulted by the Harmony patch on <c>EntityPlayerShapeRenderer</c>
    /// to suppress local-player rendering during viewfinder mode and viewport exposure accumulation.
    /// Only ever read or written on the main game thread.
    /// </summary>
    internal static class ViewportExposureSuppressContext
    {
        /// <summary>
        /// True while a virtual camera render is in progress.
        /// Consulted by renderers that must not contribute geometry to virtual captures
        /// (e.g., the mounted camera block entity renderer).
        /// </summary>
        internal static bool IsVirtualRender;
        /// <summary>
        /// Block position of the mounted camera the local player is currently shooting through, or
        /// <see langword="null"/> when not mounted. Only that one camera is hidden from the virtual
        /// capture; every other mounted camera (the player's own idle ones and other players') stays
        /// visible so they appear in the exposure.
        /// </summary>
        internal static BlockPos? ActiveMountedCameraPos;
        /// <summary>True while the player is in viewfinder mode (RMB held or exposure keeping it alive).</summary>
        internal static bool ViewfinderActive;
        /// <summary>True while the viewport exposure accumulator is actively gathering frames.</summary>
        internal static bool ExposureCapturing;
        /// <summary>When <see langword="true"/>, the patched renderer skips drawing the local player for the current frame.</summary>
        internal static bool SuppressLocalPlayer => ViewfinderActive || ExposureCapturing;
    }
}
