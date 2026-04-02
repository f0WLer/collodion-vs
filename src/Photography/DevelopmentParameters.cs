namespace Collodion
{
    /// <summary>
    /// Per-process parameters for the exposed → developed → finished pipeline in the development tray.
    /// Portion codes are "domain:path" strings. Amounts use the same unit system as WetPlateChemicalUtil.
    /// </summary>
    public sealed class DevelopmentParameters
    {
        /// <summary>Item code of the required developer portion (e.g. "collodion:developerportion").</summary>
        public string DeveloperPortionCode { get; }

        /// <summary>Number of developer applications needed to reach <see cref="PlateStage.Developed"/>.</summary>
        public int DeveloperPourCount { get; }

        /// <summary>Units consumed per developer application.</summary>
        public int DeveloperAmountPerPour { get; }

        /// <summary>Item code of the required fixer portion (e.g. "collodion:fixerportion").</summary>
        public string FixerPortionCode { get; }

        /// <summary>Units consumed per fixer application.</summary>
        public int FixerAmountPerPour { get; }

        /// <summary>When true, a water rinse after fixing reclaims the base plate.</summary>
        public bool RequiresWaterRinse { get; }

        /// <summary>
        /// Multiplier applied to the server config wet duration for sensitized/exposed plates.
        /// 0.0 = plate never dries; 1.0 = uses the config duration unchanged.
        /// </summary>
        public double WetDurationMultiplier { get; }

        public DevelopmentParameters(
            string developerPortionCode,
            int developerPourCount,
            int developerAmountPerPour,
            string fixerPortionCode,
            int fixerAmountPerPour,
            bool requiresWaterRinse,
            double wetDurationMultiplier = 1.0)
        {
            DeveloperPortionCode = developerPortionCode;
            DeveloperPourCount = developerPourCount;
            DeveloperAmountPerPour = developerAmountPerPour;
            FixerPortionCode = fixerPortionCode;
            FixerAmountPerPour = fixerAmountPerPour;
            RequiresWaterRinse = requiresWaterRinse;
            WetDurationMultiplier = wetDurationMultiplier;
        }
    }
}
