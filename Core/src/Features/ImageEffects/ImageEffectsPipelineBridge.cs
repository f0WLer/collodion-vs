using SkiaSharp;

namespace Photochemistry.ImageEffects
{
    // Thin seam for capture/render callers so effects runtime internals can move without touching callsites.
    // The effects config itself now comes from each chemistry's profile (ChemistryProfileRegistry), so this
    // no longer loads or resolves a shared baseline.
    internal static class ImageEffectsPipelineBridge
    {
        // Master toggle for the post-exposure spatial effects (grain, vignette, halation, lens softness,
        // coating unevenness, dust/scratches, edge toning). On = they bake into sealed photos. Set to false
        // to skip all of them on every output path (handheld, tray seal, screenshot). Previews are unaffected.
        internal static bool ApplyEffects = true;

        // Applies the effects pipeline in-place for one captured bitmap, using the supplied chemistry profile.
        internal static void ApplyCaptureEffects(SKBitmap bitmap, string seedKey, ImageEffectsConfig profile)
        {
            if (!ApplyEffects) return;
            ImageEffects.ApplyInPlace(bitmap, seedKey, profile);
        }
    }
}
