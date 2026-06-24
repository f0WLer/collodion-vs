using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace Photochemistry.Exposure
{
    internal static class ExposureBlit
    {
        // Y-flip corrects OpenGL's bottom-left origin to top-left for CPU readback.
        internal static void BlitYFlipped(FrameBufferRef fromFbo, FrameBufferRef toFbo)
            => BlitYFlipped(fromFbo.FboId, fromFbo.Width, fromFbo.Height, toFbo);

        // Overload for raw GL IDs — used when the source is the default back-buffer (ID 0).
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
