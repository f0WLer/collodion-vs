using Photocore.ImageEffects;
using Photocore.Plates.Rendering;

namespace Photocore.Exposure
{
    // The single place that seeds default values — nothing else hardcodes per-chemistry parameters.
    internal static class ChemistryProfileSeeder
    {
        // Returns true when something was added so the caller can persist the file.
        internal static bool EnsureComplete(Dictionary<string, ChemistryProfile> profiles, IReadOnlyList<string> chemistries)
        {
            bool changed = false;
            foreach (string code in chemistries)
            {
                string key = code.ToLowerInvariant();
                if (!profiles.TryGetValue(key, out ChemistryProfile? prof))
                {
                    profiles[key] = DefaultFor(key);
                    changed = true;
                    continue;
                }

                if (prof.ExposurePhysics == null) { prof.ExposurePhysics = DefaultExposure(key); changed = true; }
                if (prof.PostEffects == null)     { prof.PostEffects = new ImageEffectsConfig(); changed = true; }
                if (prof.Presentation == null)    { prof.Presentation = DefaultPresentation(key); changed = true; }
            }
            return changed;
        }

        internal static ChemistryProfile DefaultFor(string chemistry) => new()
        {
            ExposurePhysics = DefaultExposure(chemistry),
            PostEffects = new ImageEffectsConfig(),
            Presentation = DefaultPresentation(chemistry),
        };

        // Materialises to concrete values so no NaN inherit-sentinels survive into the persisted file.
        private static ChemistryOverrides DefaultExposure(string chemistry)
            => new ChemistryOverrides().Materialize(EmulsionProfile.Resolve(chemistry));

        private static PresentationSettings DefaultPresentation(string chemistry)
        {
            PlatePresentation seed = PlatePresentation.SeedFor(chemistry);
            return new PresentationSettings
            {
                DepositR = seed.DepositR,
                DepositG = seed.DepositG,
                DepositB = seed.DepositB,
                DensityGamma = seed.DensityGamma,
            };
        }
    }
}
