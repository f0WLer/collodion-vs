using SkiaSharp;
using Photochemistry.ImageEffects;
using Photochemistry.PhotoSync.Storage;

namespace Photochemistry.Exposure
{
    // The caller supplies finishing effects so the seal has no plate/player knowledge.
    internal static class ExposureSeal
    {
        // seedKey seeds deterministic finishing effects — pass a distinct key per call site so grain patterns don't alias.
        internal static string ToPhoto(
            GpuExposureAccumulator accumulator,
            int maxDimension,
            string seedKey,
            ImageEffectsConfig effects,
            bool applyFinishing = true)
        {
            using SKBitmap resolved = accumulator.Resolve();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(resolved, maxDimension);
            try
            {
                if (applyFinishing)
                {
                    ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, seedKey, effects);
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
