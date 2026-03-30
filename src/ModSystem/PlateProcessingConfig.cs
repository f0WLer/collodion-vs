namespace Collodion
{
    public sealed class PlateProcessingConfig
    {
        public const double DefaultWetPlateDurationHours = 0.66;

        public string Comment_DevelopmentTrayChemicalUnitsPerUse = "Developer/fixer units consumed per tray pour. Lower = cheaper processing, higher = costlier.";
        public int DevelopmentTrayChemicalUnitsPerUse = 40;

        public string Comment_PolishSeconds = "Hold duration to polish rough plates. 0 = instant polish.";
        public float PolishSeconds = 2f;

        public string Comment_CoatSeconds = "Hold duration to coat clean plates with collodion. 0 = instant coating.";
        public float CoatSeconds = 2.5f;

        public string Comment_CollodionUnitsPerCoat = "Collodion units consumed for one coating action.";
        public int CollodionUnitsPerCoat = 5;

        public string Comment_ConsumePlainClothOnPolish = "If true, polishing consumes plain cloth per action.";
        public bool ConsumePlainClothOnPolish = false;

        public string Comment_PlainClothConsumedPerPolish = "Plain cloth consumed per polish when ConsumePlainClothOnPolish is true.";
        public int PlainClothConsumedPerPolish = 1;

        public string Comment_WetPlateDurationHours = "How long a freshly-coated silvered plate stays wet, in in-game hours. This is affected by the world's time speed. Default 0.66 (40 minutes). Server-authoritative.";
        public double WetPlateDurationHours = DefaultWetPlateDurationHours;

        internal void ClampInPlace()
        {
            if (DevelopmentTrayChemicalUnitsPerUse < 1) DevelopmentTrayChemicalUnitsPerUse = 1;
            if (DevelopmentTrayChemicalUnitsPerUse > 5000) DevelopmentTrayChemicalUnitsPerUse = 5000;

            if (PolishSeconds < 0f) PolishSeconds = 0f;
            if (PolishSeconds > 30f) PolishSeconds = 30f;

            if (CoatSeconds < 0f) CoatSeconds = 0f;
            if (CoatSeconds > 30f) CoatSeconds = 30f;

            if (CollodionUnitsPerCoat < 1) CollodionUnitsPerCoat = 1;
            if (CollodionUnitsPerCoat > 5000) CollodionUnitsPerCoat = 5000;

            if (PlainClothConsumedPerPolish < 0) PlainClothConsumedPerPolish = 0;
            if (PlainClothConsumedPerPolish > 64) PlainClothConsumedPerPolish = 64;

            if (WetPlateDurationHours < 0.01) WetPlateDurationHours = 0.01;
            if (WetPlateDurationHours > 720.0) WetPlateDurationHours = 720.0;
        }
    }
}
