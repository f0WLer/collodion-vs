using SkiaSharp;

namespace Photochemistry.ImageEffects
{
    // Thin seam for capture/render callers: gives them a single, unambiguous entry point into the effects
    // runtime (the runtime class is itself named ImageEffects inside this namespace, so callers in other
    // namespaces can't reference it cleanly). The effects config comes from each chemistry's profile
    // (ChemistryProfileRegistry), so this no longer loads or resolves a shared baseline.
    internal static class ImageEffectsPipelineBridge
    {
        // Applies the effects pipeline in-place for one captured bitmap, using the supplied chemistry profile.
        internal static void ApplyCaptureEffects(SKBitmap bitmap, string seedKey, ImageEffectsConfig profile)
            => ImageEffects.ApplyInPlace(bitmap, seedKey, profile);
    }
}
