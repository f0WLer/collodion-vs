using SkiaSharp;
using Vintagestory.API.Client;

namespace Photochemistry.Plates.Rendering
{
    internal static class PhotoImageProcessor
    {
        // 256-entry lookup: raw density (luminance, 0..255) -> lifted silver alpha (0..255), built per call
        // from the resolved presentation's density gamma so the per-pixel loops index it instead of calling
        // Pow. Identity when gamma ~= 1. The silver/print colour and gamma are no longer hardcoded here —
        // every caller passes the chemistry's resolved PlatePresentation (tone + gamma come from config).
        private static byte[] BuildDensityLut(float gamma)
        {
            var lut = new byte[256];
            bool identity = MathF.Abs(gamma - 1f) < 1e-4f;
            for (int i = 0; i < 256; i++)
            {
                if (identity) { lut[i] = (byte)i; continue; }
                float lifted = MathF.Pow(i / 255f, gamma) * 255f;
                if (lifted < 0f) lifted = 0f;
                if (lifted > 255f) lifted = 255f;
                lut[i] = (byte)(lifted + 0.5f);
            }
            return lut;
        }

        // Reads PNG width/height directly from IHDR bytes without full image decode.
        internal static bool TryGetPngDimensions(byte[] pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (pngBytes == null || pngBytes.Length < 24) return false;

            if (pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47
                || pngBytes[4] != 0x0D || pngBytes[5] != 0x0A || pngBytes[6] != 0x1A || pngBytes[7] != 0x0A)
            {
                return false;
            }

            try
            {
                width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
                height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
            }
            catch
            {
                width = 0;
                height = 0;
                return false;
            }

            return width > 0 && height > 0;
        }

        // Ensures a derived photo variant exists and is up-to-date with source/effect inputs.
        // The presentation selects the physical look: the silver-over-black glass density map (default)
        // or the opaque reddish-brown positive of a salted-paper print.
        internal static bool TryEnsureDerivedPhoto(ICoreClientAPI capi, string sourcePath, string derivedPath, string seedKey, bool useDevelopedStage, int developPours, int maxDeveloperPours, PlatePresentation presentation)
        {
            try
            {
                // Reuse existing derived image when source has not changed.
                if (File.Exists(derivedPath))
                {
                    try
                    {
                        DateTime srcTime = File.GetLastWriteTimeUtc(sourcePath);
                        DateTime dstTime = File.GetLastWriteTimeUtc(derivedPath);
                        if (dstTime >= srcTime) return true;
                    }
                    catch
                    {
                        // If time checks fail, fall through and re-generate.
                    }
                }

                using var decoded = SKBitmap.Decode(sourcePath);
                if (decoded == null) return false;

                // Build an explicit Rgba8888/Unpremul working bitmap so the density value we
                // write into the alpha channel survives encoding as a real RGBA PNG.
                //
                // Two SkiaSharp pitfalls this avoids (both verified to drop alpha -> IHDR
                // colortype 2 / channel-less RGB, rendering every pixel solid dark silver):
                //   1. SKBitmap.Decode of an opaque photo returns Bgra8888/Opaque. CopyTo into
                //      a pre-allocated Unpremul bitmap RECONFIGURES the destination back to the
                //      source's Bgra8888/Opaque metadata, so our Unpremul allocation is lost.
                //   2. SKImage.FromBitmap re-reads the bitmap's (Opaque) alpha type and encodes
                //      RGB-only. We instead convert via SKImage.ReadPixels into a buffer we own
                //      as Unpremul, and encode via SKImage.FromPixelCopy with explicit info.
                var targetInfo = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using var src = new SKBitmap();
                if (!src.TryAllocPixels(targetInfo)) return false;
                using (var decodedImage = SKImage.FromBitmap(decoded))
                {
                    if (decodedImage == null || !decodedImage.ReadPixels(targetInfo, src.GetPixels(), src.RowBytes))
                        return false;
                }

                float t = maxDeveloperPours <= 1 ? 1f : (developPours - 1) / (float)(maxDeveloperPours - 1);
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;

                if (useDevelopedStage)
                {
                    if (presentation.Medium == PresentationMedium.PaperPrint)
                    {
                        // Salted-paper print: an opaque positive composited over the paper base.
                        BuildPaperPositiveMap(src, presentation, t);
                    }
                    else
                    {
                        // Glass plate: convert the positive source into a silver density map:
                        // alpha = density (bright source = dense silver = opaque), RGB = dark silver color.
                        // Both the silver tone and the density curve come from the resolved presentation,
                        // so each chemistry develops to its own image colour and contrast.
                        byte[] lut = BuildDensityLut(presentation.DensityGamma);
                        InvertToNegativeDensityMap(src, presentation.DepositR, presentation.DepositG, presentation.DepositB, lut);

                        // During development (pours 1-4), scale back silver visibility progressively.
                        // At t=1.0 (pour 5 / Developed / Finished) the full density map is kept as-is.
                        if (t < 0.999f)
                            ApplyNegativeSilverVisuals(src, t);
                    }
                }

                // Encode with explicit Unpremul info so the alpha channel survives as a true
                // RGBA PNG (IHDR colortype 6). SKImage.FromBitmap would re-read src's metadata
                // and can drop alpha; FromPixelCopy with our info guarantees the density map.
                using var image = SKImage.FromPixelCopy(targetInfo, src.GetPixels(), src.RowBytes);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);

                Directory.CreateDirectory(Path.GetDirectoryName(derivedPath)!);
                File.WriteAllBytes(derivedPath, data.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                capi.Logger.Warning($"photochemistry: failed to build derived photo '{derivedPath}': {ex.Message}");
                return false;
            }
        }

        // Bakes a flat, viewable "ambrotype positive" PNG from the raw positive source: the
        // silver density map flattened over an opaque black backing. Each pixel becomes
        // depositColor * density (density = luminance), fully opaque — identical to what the
        // framed silver-over-black composite shows, but as a standalone file for viewing
        // outside the game. The deposit colour and density gamma come from the resolved
        // presentation (the photo's chemistry), not a hardcoded silver.
        // Returns false on any failure (no partial file is left behind).
        internal static bool TryWriteCompositePng(string sourcePath, string outPath, PlatePresentation presentation)
        {
            try
            {
                byte[] densityLut = BuildDensityLut(presentation.DensityGamma);
                byte depR = presentation.DepositR, depG = presentation.DepositG, depB = presentation.DepositB;

                using var decoded = SKBitmap.Decode(sourcePath);
                if (decoded == null) return false;

                var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                using var dst = new SKBitmap();
                if (!dst.TryAllocPixels(info)) return false;
                using (var decodedImage = SKImage.FromBitmap(decoded))
                {
                    if (decodedImage == null || !decodedImage.ReadPixels(info, dst.GetPixels(), dst.RowBytes))
                        return false;
                }

                int w = dst.Width;
                int h = dst.Height;
                // dst is always allocated Rgba8888/4bpp (above), so the unsafe fast path always applies;
                // bail safely (no file written) for any unexpected format rather than carry a slow duplicate.
                SKPixmap pixmap = dst.PeekPixels();
                if (pixmap == null || pixmap.BytesPerPixel != 4 || pixmap.ColorType != SKColorType.Rgba8888)
                    return false;
                unsafe
                {
                    byte* basePtr = (byte*)pixmap.GetPixels().ToPointer();
                    int rowBytes = pixmap.RowBytes;
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = basePtr + y * rowBytes;
                        for (int x = 0; x < w; x++)
                        {
                            int i = x * 4;
                            float density = (0.299f * row[i + 0] + 0.587f * row[i + 1] + 0.114f * row[i + 2]) / 255f;
                            float a = densityLut[(int)(density * 255f)] / 255f;
                            row[i + 0] = (byte)(depR * a);
                            row[i + 1] = (byte)(depG * a);
                            row[i + 2] = (byte)(depB * a);
                            row[i + 3] = 255;
                        }
                    }
                }

                using var image = SKImage.FromPixelCopy(info, dst.GetPixels(), dst.RowBytes);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);

                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, data.ToArray());
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Converts a positive source bitmap in-place to a silver density map: alpha = luminance (exposure
        // density, opaque where the scene was bright), RGB = the chemistry's deposit colour. The plate is one
        // physical silver-on-glass image — against the world the clear glass shows through, while over a black
        // backing the same map reads as a positive (ambrotype). Deposit colour and density curve are passed in
        // from the resolved presentation, so the tone is per-chemistry rather than fixed here.
        private static void InvertToNegativeDensityMap(SKBitmap bmp, byte silverR, byte silverG, byte silverB, byte[] densityLut)
        {
            if (bmp == null) return;

            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            // The working bitmap is always allocated Rgba8888/4bpp (see TryEnsureDerivedPhoto), so the
            // unsafe fast path always applies; the guard is a defensive no-op for any unexpected format.
            SKPixmap pixmap = bmp.PeekPixels();
            SKColorType ct = pixmap?.ColorType ?? SKColorType.Unknown;
            if (pixmap == null || pixmap.BytesPerPixel != 4 || (ct != SKColorType.Bgra8888 && ct != SKColorType.Rgba8888))
                return;

            bool bgra = ct == SKColorType.Bgra8888;

            unsafe
            {
                byte* basePtr = (byte*)pixmap.GetPixels().ToPointer();
                int rowBytes = pixmap.RowBytes;

                for (int y = 0; y < h; y++)
                {
                    byte* row = basePtr + y * rowBytes;

                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4;
                        float r, g, b;

                        if (bgra)
                        {
                            b = row[i + 0] / 255f;
                            g = row[i + 1] / 255f;
                            r = row[i + 2] / 255f;
                        }
                        else
                        {
                            r = row[i + 0] / 255f;
                            g = row[i + 1] / 255f;
                            b = row[i + 2] / 255f;
                        }

                        float density = 0.299f * r + 0.587f * g + 0.114f * b;
                        byte alpha = densityLut[(int)(density * 255f)];

                        if (bgra)
                        {
                            row[i + 0] = silverB;
                            row[i + 1] = silverG;
                            row[i + 2] = silverR;
                            row[i + 3] = alpha;
                        }
                        else
                        {
                            row[i + 0] = silverR;
                            row[i + 1] = silverG;
                            row[i + 2] = silverB;
                            row[i + 3] = alpha;
                        }
                    }
                }
            }
        }

        // Warm paper-base colour the salted print is composited over (the unexposed sheet).
        private const byte PaperBaseR = 235, PaperBaseG = 228, PaperBaseB = 210;

        // Converts a positive source bitmap in-place into an opaque salted-paper print:
        // for each pixel the print density (dark scene = heavy deposit) blends the warm reddish-brown
        // deposit colour over the paper base. Fully opaque — a reflective positive, not a glass density map.
        // t (0..1) is development progress: a faint deposit early, full strength at t=1.
        private static void BuildPaperPositiveMap(SKBitmap bmp, PlatePresentation presentation, float t)
        {
            if (bmp == null) return;
            int w = bmp.Width, h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            float gamma = presentation.DensityGamma <= 0f ? 1f : presentation.DensityGamma;
            float strength = t < 0f ? 0f : (t > 1f ? 1f : t);
            byte depR = presentation.DepositR, depG = presentation.DepositG, depB = presentation.DepositB;

            // Fast path always applies for the allocated Rgba8888 working bitmap; defensive no-op otherwise.
            SKPixmap pixmap = bmp.PeekPixels();
            SKColorType ct = pixmap?.ColorType ?? SKColorType.Unknown;
            if (pixmap == null || pixmap.BytesPerPixel != 4 || (ct != SKColorType.Bgra8888 && ct != SKColorType.Rgba8888))
                return;

            bool bgra = ct == SKColorType.Bgra8888;
            unsafe
            {
                byte* basePtr = (byte*)pixmap.GetPixels().ToPointer();
                int rowBytes = pixmap.RowBytes;
                for (int y = 0; y < h; y++)
                {
                    byte* row = basePtr + y * rowBytes;
                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4;
                        float r = (bgra ? row[i + 2] : row[i + 0]) / 255f;
                        float g = row[i + 1] / 255f;
                        float b = (bgra ? row[i + 0] : row[i + 2]) / 255f;
                        SKColor outc = PaperPixel(r, g, b, gamma, strength, depR, depG, depB);
                        if (bgra) { row[i + 0] = outc.Blue; row[i + 1] = outc.Green; row[i + 2] = outc.Red; }
                        else      { row[i + 0] = outc.Red;  row[i + 1] = outc.Green; row[i + 2] = outc.Blue; }
                        row[i + 3] = 255;
                    }
                }
            }
        }

        // Single salted-paper pixel: deposit = (1 - luminance)^gamma * strength, blended over the paper base.
        private static SKColor PaperPixel(float r, float g, float b, float gamma, float strength, byte depR, byte depG, byte depB)
        {
            float lum = 0.299f * r + 0.587f * g + 0.114f * b;
            float deposit = MathF.Pow(1f - lum, gamma) * strength;
            if (deposit < 0f) deposit = 0f; else if (deposit > 1f) deposit = 1f;
            byte or = (byte)(PaperBaseR + (depR - PaperBaseR) * deposit);
            byte og = (byte)(PaperBaseG + (depG - PaperBaseG) * deposit);
            byte ob = (byte)(PaperBaseB + (depB - PaperBaseB) * deposit);
            return new SKColor(or, og, ob, 255);
        }

        // Scales back silver density visibility during progressive development (pours 1–4).
        // Operates on the RGBA density map produced by InvertToNegativeDensityMap.
        // At t=0 (pour 1): only the highest-density (most-exposed) pixels show any silver, at low opacity.
        // At t→1 (pour 5): all density passes the gate, full alpha restored — full negative visible.
        private static void ApplyNegativeSilverVisuals(SKBitmap bmp, float t)
        {
            if (bmp == null) return;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            // Smoothstep the progress so the reveal is middle-heavy: a faint first pour,
            // the bulk of the image emerging across pours 2-4, and only the deepest shadows
            // left to fill on the final pour (avoids the abrupt jump at the last step).
            float te = t * t * (3f - 2f * t);

            // Density gate: at te=0 only top 15% density passes; at te=1 everything passes.
            float gate = Lerp(0.85f, 0f, te);
            // Max alpha for pixels that pass the gate: faint at first, full at te=1.
            float maxAlpha = Lerp(0.4f, 1f, te);
            // Edge fade: early pours develop center first; corners fill in last.
            float edgeFade = Lerp(0.55f, 0f, te);

            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            float invW = 1f / w;
            float invH = 1f / h;
            const float invCorner = 1f / 0.7071f;

            float[] nx2 = new float[w];
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f) * invW - 0.5f;
                nx2[x] = nx * nx;
            }

            float[] ny2 = new float[h];
            for (int y = 0; y < h; y++)
            {
                float ny = (y + 0.5f) * invH - 0.5f;
                ny2[y] = ny * ny;
            }

            float gateRange = 1f - gate;
            if (gateRange < 0.001f) gateRange = 0.001f;

            // Fast path always applies for the allocated Rgba8888 working bitmap; defensive no-op otherwise.
            SKPixmap pixmap = bmp.PeekPixels();
            SKColorType ct = pixmap?.ColorType ?? SKColorType.Unknown;
            if (pixmap == null || pixmap.BytesPerPixel != 4 || (ct != SKColorType.Bgra8888 && ct != SKColorType.Rgba8888))
                return;

            bool bgra = ct == SKColorType.Bgra8888;
            bool doEdge = edgeFade > 0f;

            unsafe
            {
                byte* basePtr = (byte*)pixmap.GetPixels().ToPointer();
                int rowBytes = pixmap.RowBytes;

                for (int y = 0; y < h; y++)
                {
                    byte* row = basePtr + y * rowBytes;
                    float yTerm = ny2[y];

                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4;
                        float density = row[i + 3] / 255f;

                        float visible = density > gate ? (density - gate) / gateRange : 0f;
                        float effectiveAlpha = visible * maxAlpha;

                        if (doEdge)
                        {
                            float edge = (float)Math.Sqrt(nx2[x] + yTerm) * invCorner;
                            if (edge > 1f) edge = 1f;
                            effectiveAlpha *= (1f - edge * edgeFade);
                        }

                        if (effectiveAlpha < 0f) effectiveAlpha = 0f;
                        if (effectiveAlpha > 1f) effectiveAlpha = 1f;

                        row[i + 3] = (byte)(effectiveAlpha * 255f);
                        // RGB (silver color) is unchanged.
                    }
                }
            }
        }

        // Clamped linear interpolation helper.
        private static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }
    }
}
