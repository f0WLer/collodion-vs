using SkiaSharp;

namespace Photocore.ImageEffects
{
    public static partial class ImageEffects
    {
        // Adds a warm sepia-toned border that fades toward the centre, mimicking uneven toning / edge
        // oxidation on an aging plate. This is a spatial aging artifact only — the uniform image colour is
        // owned by the per-chemistry silver tone, not here. Driven by EdgeWarmth (0 = no border tint).
        private static void ApplyEdgeWarmth(SKBitmap bmp, ImageEffectsConfig cfg)
        {
            float edgeWarm = Clamp01(cfg.EdgeWarmth);
            if (edgeWarm <= 0.001f) return;

            int w = bmp.Width;
            int h = bmp.Height;
            IntPtr ptr = bmp.GetPixels();
            if (ptr == IntPtr.Zero) return;

            int count = w * h;
            byte[] bytes = new byte[count * 4];
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, bytes.Length);

            float edgeWidthPx = Math.Max(cfg.EdgeWarmthWidthMinPx, Math.Min(w, h) * cfg.EdgeWarmthWidthFraction);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    float b = bytes[i + 0] / 255f;
                    float g = bytes[i + 1] / 255f;
                    float r = bytes[i + 2] / 255f;

                    // Warm (sepia) version of this pixel.
                    float sr = Clamp01(r * 0.393f + g * 0.769f + b * 0.189f);
                    float sg = Clamp01(r * 0.349f + g * 0.686f + b * 0.168f);
                    float sb = Clamp01(r * 0.272f + g * 0.534f + b * 0.131f);

                    // Border falloff: 1 at the very edge, 0 past edgeWidthPx inward (smoothstepped).
                    float distToEdge = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y));
                    float edge = Clamp01(1f - (distToEdge / edgeWidthPx));
                    edge = edge * edge * (3f - 2f * edge);
                    float blend = Clamp01(cfg.EdgeWarmthBlendScale * edgeWarm * edge);

                    r = r * (1f - blend) + sr * blend;
                    g = g * (1f - blend) + sg * blend;
                    b = b * (1f - blend) + sb * blend;

                    bytes[i + 0] = (byte)(b * 255f);
                    bytes[i + 1] = (byte)(g * 255f);
                    bytes[i + 2] = (byte)(r * 255f);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }
    }
}
