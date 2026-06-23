using Photochemistry.ImageEffects;
using Photochemistry.Plates;
using Photochemistry.Plates.Rendering;

namespace Photochemistry.Exposure
{
    /// <summary>
    /// The single place that produces default chemistry profiles and fills any gaps. Every default value in
    /// the system flows from here: exposure physics from the hardcoded <see cref="PlateProcessProfile"/>,
    /// presentation tone from the hardcoded <see cref="PlatePresentation"/> seeds, and post-effects from the
    /// <see cref="ImageEffectsConfig"/> code defaults. Nothing else hardcodes a per-chemistry value — handlers
    /// read the resolved profile, and missing pieces are seeded here on load.
    /// </summary>
    internal static class ChemistryProfileSeeder
    {
        /// <summary>
        /// Ensures every registered chemistry has a complete profile, adding any missing chemistry or section
        /// from the central defaults. Returns true when something was added so the caller can persist the file.
        /// </summary>
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

        /// <summary>A complete default profile for one chemistry, seeded entirely from the central defaults.</summary>
        internal static ChemistryProfile DefaultFor(string chemistry) => new()
        {
            ExposurePhysics = DefaultExposure(chemistry),
            PostEffects = new ImageEffectsConfig(),
            Presentation = DefaultPresentation(chemistry),
        };

        // Exposure seed: the hardcoded process profile materialised to concrete values (no inherit sentinels).
        private static ChemistryOverrides DefaultExposure(string chemistry)
            => new ChemistryOverrides().Materialize(PlateProcessProfile.Resolve(chemistry));

        // Presentation seed: the hardcoded silver/print tone for this chemistry's default developed look.
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
