namespace Collodion
{
    public sealed class CollodionConfig
    {
        public CollodionClientConfig Client = new CollodionClientConfig();
        public WetplateEffectsConfig Effects = new WetplateEffectsConfig();
        public WetplateEffectsConfig EffectsDeveloped = CreateDevelopedEffectsDefaults();
        public PhotographConfig Photograph = new PhotographConfig();
        public PlateProcessingConfig PlateProcessing = new PlateProcessingConfig();
        public PhotoSyncConfig PhotoSync = new PhotoSyncConfig();
        public PhotoCapturePipelineConfig PhotoCapturePipeline = new PhotoCapturePipelineConfig();

        // Viewfinder capture behavior (capture runs client-side; server provides authoritative limits in multiplayer).
        public ViewfinderConfig Viewfinder = new ViewfinderConfig();

        // Timed interaction configuration (shared by client/server).
        public DevelopmentTrayInteractionConfig DevelopmentTrayInteractions = new DevelopmentTrayInteractionConfig();

        // Optional presets (editable via .collodion effects preset ...)
        public WetplateEffectsConfig EffectsPresetIndoor = new WetplateEffectsConfig();
        public WetplateEffectsConfig EffectsPresetOutdoor = new WetplateEffectsConfig();

        internal void ClampInPlace()
        {
            Client ??= new CollodionClientConfig();
            Client.ClampInPlace();

            Effects ??= new WetplateEffectsConfig();
            Effects.ClampInPlace();

            EffectsDeveloped ??= CreateDevelopedEffectsDefaults();
            EffectsDeveloped.ClampInPlace();

            Photograph ??= new PhotographConfig();
            Photograph.ClampInPlace();

            PlateProcessing ??= new PlateProcessingConfig();
            PlateProcessing.ClampInPlace();

            PhotoSync ??= new PhotoSyncConfig();
            PhotoSync.ClampInPlace();

            PhotoCapturePipeline ??= new PhotoCapturePipelineConfig();
            PhotoCapturePipeline.ClampInPlace();

            EffectsPresetIndoor ??= new WetplateEffectsConfig();
            EffectsPresetIndoor.ClampInPlace();

            EffectsPresetOutdoor ??= new WetplateEffectsConfig();
            EffectsPresetOutdoor.ClampInPlace();

            DevelopmentTrayInteractions ??= new DevelopmentTrayInteractionConfig();
            DevelopmentTrayInteractions.ClampInPlace();

            Viewfinder ??= new ViewfinderConfig();
            Viewfinder.ClampInPlace();
        }

        internal static WetplateEffectsConfig CreateDevelopedEffectsDefaults()
        {
            var cfg = new WetplateEffectsConfig();
            cfg.ClampInPlace();
            return cfg;
        }
    }

}
