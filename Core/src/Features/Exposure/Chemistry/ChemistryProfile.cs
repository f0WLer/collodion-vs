using Photocore.ImageEffects;

namespace Photocore.Exposure
{
    // Every section is non-null — ChemistryProfileSeeder fills any gaps before the registry is used.
    internal sealed class ChemistryProfile
    {
        public ChemistryOverrides ExposurePhysics { get; set; } = new();
        public ImageEffectsConfig PostEffects { get; set; } = new();
        public PresentationSettings Presentation { get; set; } = new();
    }
}
