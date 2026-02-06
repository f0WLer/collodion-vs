using System;

namespace Collodion
{
    public sealed class WetplateEffectsConfig
    {
        public bool Enabled = true;

        // If true, convert to greyscale after color adjustments.
        public bool Greyscale = true;

        // Applied before greyscale conversion (and before sepia). 1 = unchanged.
        // Use this to bias channels slightly before greyscale, e.g. boost blue and reduce red.
        public float PreGrayRed = 0.92f;
        public float PreGrayGreen = 1.0f;
        public float PreGrayBlue = 1.18f;

        // Realism tuning (0..1). Keep subtle; these are meant to break the "perfect filter" feel.
        // Imperfection: biases dust toward edges and adds slight one-sided density pooling.
        public float Imperfection = 0.60f;

        // 0..1: makes edges slightly warmer / more sepia than the center.
        public float EdgeWarmth = 0.12f;

        // 0..1: adds subtle non-uniformity (mottle/banding/density shift) near the top of the frame.
        public float SkyUnevenness = 0.30f;

        // 0..1: small edge-preserving micro blur to soften thin geometry (e.g. leaves) without killing trunks.
        public float MicroBlur = 0.18f;

        // Fraction (0..1) of the image height treated as "sky/top area" for SkyUnevenness.
        public float SkyTopFraction = 0.50f;

        // 0..1 blend of sepia tone over the original.
        public float SepiaStrength = 0.07f;

        // Contrast multiplier (1 = unchanged).
        public float Contrast = 1.32f;

        // 0..1: minimum luminance floor applied after tone mapping.
        // Real wet plates rarely hit true black; this prevents "void blacks" in dark scenes.
        public float ShadowFloor = 0.035f;

        // 0..1: luminance below this is protected from contrast (shadows compress instead of deepen).
        public float ContrastStart = 0.38f;

        // 0..1 shoulder strength for highlights. Higher = more rolloff/compression.
        public float HighlightShoulder = 0.60f;

        // 0..1 start point for shoulder rolloff (where highlight compression begins).
        public float HighlightThreshold = 0.65f;

        // Brightness offset in [-1..1] where 0 = unchanged.
        public float Brightness = 0.065f;

        // 0..1 vignette intensity.
        public float Vignette = 0.24f;

        // 0..1 sky blowout/bloom strength (applied mainly to the top of the frame).
        public float SkyBlowout = 0.40f;

        // 0..1 film grain intensity.
        public float Grain = 0.08f;

        // Decorative artifacts.
        public int DustCount = 80;
        public int ScratchCount = 5;

        // 0..1
        public float DustOpacity = 0.07f;
        public float ScratchOpacity = 0.02f;

        // Per-photo dynamic variation (deterministic from photo id).
        // DynamicScale is a +/- percentage (0.05 => +/-5%) applied to select parameters.
        public bool DynamicEnabled = false;
        public float DynamicScale = 0.05f;

        internal void ClampInPlace()
        {
            SepiaStrength = Clamp01(SepiaStrength);
            Vignette = Clamp01(Vignette);
            Grain = Clamp01(Grain);
            DustOpacity = Clamp01(DustOpacity);
            ScratchOpacity = Clamp01(ScratchOpacity);

            HighlightShoulder = Clamp01(HighlightShoulder);
            HighlightThreshold = Clamp01(HighlightThreshold);
            SkyBlowout = Clamp01(SkyBlowout);

            ShadowFloor = Clamp01(ShadowFloor);
            ContrastStart = Clamp01(ContrastStart);

            Imperfection = Clamp01(Imperfection);
            EdgeWarmth = Clamp01(EdgeWarmth);
            SkyUnevenness = Clamp01(SkyUnevenness);
            MicroBlur = Clamp01(MicroBlur);
            SkyTopFraction = Clamp01(SkyTopFraction);

            PreGrayRed = ClampRange(PreGrayRed, 0f, 2f);
            PreGrayGreen = ClampRange(PreGrayGreen, 0f, 2f);
            PreGrayBlue = ClampRange(PreGrayBlue, 0f, 2f);

            Contrast = Math.Max(0.2f, Math.Min(2.5f, Contrast));
            Brightness = Math.Max(-0.5f, Math.Min(0.5f, Brightness));

            DustCount = Math.Max(0, Math.Min(3000, DustCount));
            ScratchCount = Math.Max(0, Math.Min(400, ScratchCount));

            DynamicScale = ClampRange(DynamicScale, 0f, 0.5f);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static float ClampRange(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public WetplateEffectsConfig Clone()
        {
            return (WetplateEffectsConfig)MemberwiseClone();
        }
    }
}
