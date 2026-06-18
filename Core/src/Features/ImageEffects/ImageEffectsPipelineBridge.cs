using SkiaSharp;
using Vintagestory.API.Client;

namespace Photochemistry.ImageEffects
{
    // Thin seam for capture/render callers so effects runtime internals can move without touching callsites.
    internal static class ImageEffectsPipelineBridge
    {
        // Loads the active baseline profile from the wetplate ModData profile file.
        internal static ImageEffectsConfig LoadCaptureBaseline(ICoreClientAPI capi)
        {
            return ImageEffectsProfileService.TryLoadProfile("wetplate", capi) ?? new ImageEffectsConfig();
        }

        // Chooses per-capture override profile when provided, otherwise uses the baseline snapshot.
        internal static ImageEffectsConfig ResolveCaptureProfile(ImageEffectsConfig baselineProfile, ImageEffectsConfig? effectsOverride)
        {
            return effectsOverride != null
                ? ImageEffectsProfileService.CreateRuntimeSnapshot(effectsOverride)
                : baselineProfile;
        }

        // Set to false to skip all finishing effects on every output path (handheld, tray seal, screenshot).
        // Previews are unaffected — they never apply finishing effects.
        internal static bool ApplyEffects = false;

        // Applies the effects pipeline in-place for one captured bitmap.
        internal static void ApplyCaptureEffects(SKBitmap bitmap, string seedKey, ImageEffectsConfig profile)
        {
            if (!ApplyEffects) return;
            ImageEffects.ApplyInPlace(bitmap, seedKey, profile);
        }
    }
}