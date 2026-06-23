using Photochemistry.ImageEffects;

namespace Photochemistry.Exposure
{
    /// <summary>
    /// The complete, all-encompassing profile for one photographic chemistry — the single source of truth
    /// for everything that differs per process: the exposure-accumulation physics, the post-exposure finishing
    /// effects, and the developed-image presentation tone. Persisted as one entry in chemistry-profiles.json.
    /// Every section is non-null; <see cref="ChemistryProfileSeeder"/> fills any that are missing from the
    /// central hardcoded defaults.
    /// </summary>
    internal sealed class ChemistryProfile
    {
        public ChemistryOverrides ExposurePhysics { get; set; } = new();
        public ImageEffectsConfig PostEffects { get; set; } = new();
        public PresentationSettings Presentation { get; set; } = new();
    }
}
