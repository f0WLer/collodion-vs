namespace Photocore.Configuration
{
    public sealed class PlateProcessingConfig
    {
        /// <summary>Developer/fixer units consumed per tray pour. Lower = cheaper processing, higher = costlier.</summary>
        public int DevelopmentTrayChemicalUnitsPerUse = 40;

        /// <summary>Hold duration to polish rough plates. 0 = instant polish.</summary>
        public float PolishSeconds = 2f;

        /// <summary>Hold duration for one clean-plate sensitization pour interaction. 0 = instant.</summary>
        public float SensitizationPourSeconds = 1.5f;

        /// <summary>Cloth consumed per polish action. 0 disables cloth consumption entirely.</summary>
        public int ClothConsumedPerPolish = 0;

        /// <summary>How long a freshly-sensitized plate stays wet, in in-game hours. This is affected by the world's time speed. Default 0.75 (45 minutes). Floored at 0.5 (30 minutes) so plates can't be configured into drying out almost instantly. Server-authoritative.</summary>
        public double WetDurationHours = 0.75;

        /// <summary>How fast plates dry while inside a plate box. 0 = paused (default), 1 = full open-air rate.</summary>
        public float PlateBoxDryingMultiplier = 0f;

        internal void ClampInPlace()
        {
            DevelopmentTrayChemicalUnitsPerUse = Math.Clamp(DevelopmentTrayChemicalUnitsPerUse, 1, 5000);
            PolishSeconds = Math.Clamp(PolishSeconds, 0f, 30f);
            SensitizationPourSeconds = Math.Clamp(SensitizationPourSeconds, 0f, 30f);
            ClothConsumedPerPolish = Math.Clamp(ClothConsumedPerPolish, 0, 64);
            WetDurationHours = Math.Clamp(WetDurationHours, 0.5, 720.0);
            PlateBoxDryingMultiplier = Math.Clamp(PlateBoxDryingMultiplier, 0f, 1f);
        }
    }
}
