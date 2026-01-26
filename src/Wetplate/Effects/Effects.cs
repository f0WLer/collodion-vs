using System;
using System.IO;
using SkiaSharp;
using Vintagestory.API.Client;

namespace Collodion
{
    public static partial class WetplateEffects
    {
        public static WetplateEffectsConfig LoadOrCreate(ICoreClientAPI capi)
        {
            var modSys = CollodionModSystem.ClientInstance ?? capi.ModLoader.GetModSystem<CollodionModSystem>();
            if (modSys == null)
            {
                var fallback = new WetplateEffectsConfig();
                fallback.ClampInPlace();
                return fallback;
            }

            var cfg = modSys.GetOrLoadClientConfig(capi);
            bool dirty = false;

            if (cfg.Effects == null)
            {
                cfg.Effects = new WetplateEffectsConfig();
                dirty = true;
            }

            cfg.Effects.ClampInPlace();

            if (dirty)
            {
                modSys.SaveClientConfig(capi);
            }

            return cfg.Effects;
        }

        public static void ApplyInPlace(SKBitmap bmp, string seedKey, WetplateEffectsConfig cfg)
        {
            if (bmp == null) return;
            if (cfg == null) return;
            cfg.ClampInPlace();
            if (!cfg.Enabled) return;

            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            var rng = new Random(StableHash(seedKey ?? string.Empty));

            // 1) Channel bias (orthochromatic simulation) + 2) Greyscale conversion
            using (var srcCopy = bmp.Copy())
            using (var srcImg = SKImage.FromBitmap(srcCopy))
            using (var canvas = new SKCanvas(bmp))
#pragma warning disable CS0618 // Preserve existing sampling behavior on current Skia API
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
#pragma warning restore CS0618
            {
                paint.ColorFilter = CreateBaseColorFilter(cfg);
                paint.BlendMode = SKBlendMode.Src;
                canvas.DrawImage(srcImg, new SKRect(0, 0, w, h), paint);
            }

            // 3) Nonlinear contrast curve + 4) Highlight shoulder/clipping
            ApplyToneCurveAndShoulderInPlace(bmp, cfg);

            // 5) Sky blowout/bloom + vignette
            if (cfg.SkyBlowout > 0.001f)
            {
                ApplySkyBlowout(bmp, rng, cfg);
            }

            if (cfg.Vignette > 0.001f)
            {
                using var canvas = new SKCanvas(bmp);
                using var paint = new SKPaint { IsAntialias = true };

                var center = new SKPoint(w / 2f, h / 2f);
                float radius = Math.Max(w, h) * 0.78f;

                byte a = (byte)(255 * cfg.Vignette);
                var colors = new[]
                {
                    new SKColor(0, 0, 0, 0),
                    new SKColor(0, 0, 0, a)
                };
                var pos = new[] { 0.0f, 1.0f };

                paint.Shader = SKShader.CreateRadialGradient(center, radius, colors, pos, SKShaderTileMode.Clamp);
                paint.BlendMode = SKBlendMode.Multiply;
                canvas.DrawRect(new SKRect(0, 0, w, h), paint);
            }

            // 6) Development defects (imperfection / uneven chemistry)
            // Note: micro-blur is a cheap stand-in for long-exposure motion softness.
            if (cfg.MicroBlur > 0.001f)
            {
                ApplyEdgePreservingMicroBlur(bmp, cfg.MicroBlur);
            }

            if (cfg.Imperfection > 0.001f || cfg.SkyUnevenness > 0.001f)
            {
                ApplyUnevenDensity(bmp, rng, cfg);
            }

            // 7) Grain (silver clumps / density variations)
            if (cfg.Grain > 0.001f)
            {
                ApplySilverClumpGrain(bmp, rng, cfg);
            }

            // 4) Dust + scratches
            if (cfg.DustCount > 0 || cfg.ScratchCount > 0)
            {
                using var canvas = new SKCanvas(bmp);
                DrawDust(canvas, w, h, rng, cfg);
                DrawScratches(canvas, w, h, rng, cfg);
            }

            // 9) Very light sepia (optional) at the end
            if (cfg.SepiaStrength > 0.001f)
            {
                ApplySepiaAtEnd(bmp, cfg);
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static float ClampByte(float v)
        {
            if (v < 0f) return 0f;
            if (v > 255f) return 255f;
            return v;
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                // FNV-1a 32-bit
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return (int)h;
            }
        }
    }
}
