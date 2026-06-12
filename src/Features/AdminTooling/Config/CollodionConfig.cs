using Collodion.ImageEffects;

namespace Collodion.AdminTooling
{
    // Root persisted config tree for collodion systems.
    // Aggregates subsystem configs and enforces safe ranges through ClampInPlace.
    public sealed class CollodionConfig
    {
        public CollodionClientConfig Client = new();
        public ImageEffectsConfig Effects = new();
        public PhotographConfig Photograph = new();
        public PlateProcessingConfig PlateProcessing = new();
        public PhotoSyncConfig PhotoSync = new();
        public PhotoCapturePipelineConfig PhotoCapturePipeline = new();

        // Viewfinder capture behavior (capture runs client-side; server provides authoritative limits in multiplayer).
        internal ViewfinderConfig Viewfinder = new();

        // Timed interaction configuration (shared by client/server).
        public DevelopmentTrayInteractionConfig DevelopmentTrayInteractions = new();

        // Optional presets (editable via .collodion effects preset ...)
        public ImageEffectsConfig EffectsPresetIndoor = new();
        public ImageEffectsConfig EffectsPresetOutdoor = new();

        // Clamps and initializes nested config branches so runtime access stays null-safe and bounded.
        internal void ClampInPlace()
        {
            Client ??= new CollodionClientConfig();
            Client.ClampInPlace();

            Effects ??= new ImageEffectsConfig();
            Effects.ClampInPlace();

            Photograph ??= new PhotographConfig();
            Photograph.ClampInPlace();

            PlateProcessing ??= new PlateProcessingConfig();
            PlateProcessing.ClampInPlace();

            PhotoSync ??= new PhotoSyncConfig();
            PhotoSync.ClampInPlace();

            PhotoCapturePipeline ??= new PhotoCapturePipelineConfig();
            PhotoCapturePipeline.ClampInPlace();

            EffectsPresetIndoor ??= new ImageEffectsConfig();
            EffectsPresetIndoor.ClampInPlace();

            EffectsPresetOutdoor ??= new ImageEffectsConfig();
            EffectsPresetOutdoor.ClampInPlace();

            DevelopmentTrayInteractions ??= new DevelopmentTrayInteractionConfig();
            DevelopmentTrayInteractions.ClampInPlace();

            Viewfinder ??= new ViewfinderConfig();
            Viewfinder.ClampInPlace();
        }
    }

}
