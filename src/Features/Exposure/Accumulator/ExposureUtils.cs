using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace Collodion.Exposure
{
    /// <summary>Shared utilities for the exposure pipeline: framebuffer operations.</summary>
    internal static class ExposureUtils
    {
        /// <summary>
        /// Blits <paramref name="fromFbo"/> into <paramref name="toFbo"/> with a vertical flip,
        /// converting OpenGL's bottom-left-origin image to top-left origin.
        /// </summary>
        internal static void BlitYFlipped(FrameBufferRef fromFbo, FrameBufferRef toFbo)
            => BlitYFlipped(fromFbo.FboId, fromFbo.Width, fromFbo.Height, toFbo);

        /// <summary>
        /// Overload that accepts a raw GL framebuffer ID and dimensions.
        /// Used when the source is not wrapped in a <see cref="FrameBufferRef"/> — e.g. the default back-buffer (ID 0).
        /// </summary>
        internal static void BlitYFlipped(int fromFboId, int fromW, int fromH, FrameBufferRef toFbo)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fromFboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, toFbo.FboId);
            GL.BlitFramebuffer(0, 0, fromW, fromH,
                0, toFbo.Height, toFbo.Width, 0,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        }
    }
}
