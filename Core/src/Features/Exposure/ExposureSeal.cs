using SkiaSharp;
using Photochemistry.ImageEffects;
using Photochemistry.PhotoSync.Storage;

namespace Photochemistry.Exposure
{
    /// <summary>
    /// Finalizes an exposure accumulator into a saved Photo PNG: resolve the latent image,
    /// crop to plate aspect, optionally apply wet-plate finishing, and write the file.
    /// The caller supplies the finishing profile (gameplay decides which one), so the seal
    /// itself carries no plate/player knowledge.
    /// </summary>
    internal static class ExposureSeal
    {
        /// <summary>
        /// Resolves <paramref name="accumulator"/>, crops to plate aspect within
        /// <paramref name="maxDimension"/>, applies finishing when <paramref name="applyFinishing"/>
        /// is set, and saves the PNG — returning the saved file name (usable as a photo id).
        /// <paramref name="seedKey"/> seeds deterministic finishing (grain etc.), so distinct
        /// call sites pass distinct keys to keep their grain patterns stable.
        /// </summary>
        internal static string ToPhoto(
            GpuExposureAccumulator accumulator,
            int maxDimension,
            string seedKey,
            ImageEffectsConfig baselineEffects,
            ImageEffectsConfig? effectsOverride,
            bool applyFinishing = true)
        {
            using SKBitmap resolved = accumulator.Resolve();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(resolved, maxDimension);
            try
            {
                if (applyFinishing)
                {
                    ImageEffectsConfig profile = ImageEffectsPipelineBridge.ResolveCaptureProfile(baselineEffects, effectsOverride);
                    ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, seedKey, profile);
                }

                return PhotoAssetStoragePaths.SaveExposurePng(cropped);
            }
            finally
            {
                cropped.Dispose();
            }
        }
    }
}
