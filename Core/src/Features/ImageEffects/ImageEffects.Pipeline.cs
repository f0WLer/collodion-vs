using SkiaSharp;

namespace Photochemistry.ImageEffects
{
    public static partial class ImageEffects
    {
        // Pipeline owner: orchestrates stage order for post-exposure image effects.
        // The input bitmap is already a developed silver image (resolved by the exposure accumulation physics),
        // so this pass adds only spatial/optical/material artifacts — never tone, greyscale, or contrast.
        public static void ApplyInPlace(SKBitmap bmp, string seedKey, ImageEffectsConfig cfg)
        {
            if (bmp == null) return;
            if (cfg == null) return;
            if (!cfg.Enabled) return;

            var effectiveCfg = cfg;
            if (cfg.DynamicEnabled && cfg.DynamicScale > 0f)
            {
                effectiveCfg = cfg.Clone();
                ApplyDynamicVariance(effectiveCfg, seedKey);
            }

            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            var rng = new Random(StableHash(seedKey ?? string.Empty));

            // 1) Halation — glow around bright areas from light scatter through the glass base.
            ApplyHalation(bmp, effectiveCfg);

            // 2) Sky blowout/bloom — spatial bloom around blown highlights, top of frame.
            if (effectiveCfg.SkyBlowout > 0.001f)
            {
                ApplySkyBlowout(bmp, rng, effectiveCfg);
            }

            // 3) Vignette — radial optical falloff with a faint per-frame directional bias.
            if (effectiveCfg.Vignette > 0.001f)
            {
                using var canvas = new SKCanvas(bmp);
                using var paint = new SKPaint { IsAntialias = true };

                float centerOffsetX = ((float)rng.NextDouble() * 2f - 1f) * w * 0.05f;
                float centerOffsetY = ((float)rng.NextDouble() * 2f - 1f) * h * 0.04f;
                var center = new SKPoint(w / 2f + centerOffsetX, h / 2f + centerOffsetY);
                float radius = Math.Max(w, h) * effectiveCfg.VignetteRadius;

                byte a = (byte)(255 * effectiveCfg.Vignette);
                var colors = new[]
                {
                    new SKColor(0, 0, 0, 0),
                    new SKColor(0, 0, 0, a)
                };
                var pos = new[] { 0.0f, 1.0f };

                paint.Shader = SKShader.CreateRadialGradient(center, radius, colors, pos, SKShaderTileMode.Clamp);
                paint.BlendMode = SKBlendMode.Multiply;
                canvas.DrawRect(new SKRect(0, 0, w, h), paint);

                // Add very subtle directional falloff so the vignette is not perfectly radial every frame.
                float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dx = (float)Math.Cos(ang);
                float dy = (float)Math.Sin(ang);
                float far = Math.Max(w, h) * 0.75f;
                var p0 = new SKPoint(center.X - dx * far, center.Y - dy * far);
                var p1 = new SKPoint(center.X + dx * far, center.Y + dy * far);
                byte a2 = (byte)Math.Max(0, Math.Min(255, (int)(a * 0.28f)));
                using var chemPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Multiply };
                chemPaint.Shader = SKShader.CreateLinearGradient(
                    p0,
                    p1,
                    [new SKColor(0, 0, 0, a2), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, a2)],
                    [0f, 0.5f, 1f],
                    SKShaderTileMode.Clamp);
                canvas.DrawRect(new SKRect(0, 0, w, h), chemPaint);
            }

            // 4) Development defects — one-sided density pooling + sky coating unevenness.
            if (effectiveCfg.Imperfection > 0.001f || effectiveCfg.SkyUnevenness > 0.001f)
            {
                ApplyUnevenDensity(bmp, rng, effectiveCfg);
            }

            // 5) Radial lens aberration — edge softness from uncorrected historical optics (before grain,
            //    so grain lands on the softened edges naturally).
            ApplyLensAberration(bmp, effectiveCfg);

            // 6) Grain (silver clumps / density variations).
            if (effectiveCfg.Grain > 0.001f)
            {
                ApplySilverClumpGrain(bmp, rng, effectiveCfg);
            }

            // 7) Dust + scratches.
            if (effectiveCfg.DustCount > 0 || effectiveCfg.ScratchCount > 0)
            {
                using var canvas = new SKCanvas(bmp);
                DrawDust(canvas, w, h, rng, effectiveCfg);
                DrawScratches(canvas, w, h, rng, effectiveCfg);
            }

            // 8) Warm toned border (uneven edge toning / oxidation) at the very end.
            ApplyEdgeWarmth(bmp, effectiveCfg);
        }
    }
}
