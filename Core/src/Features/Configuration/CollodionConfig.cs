namespace Photochemistry.Configuration
{
    // Root persisted config tree for collodion systems.
    // Aggregates subsystem configs and enforces safe ranges through ClampInPlace.
    public sealed class PhotochemistryConfig
    {
        public PhotochemistryClientConfig Client = new();
        public PlateProcessingConfig PlateProcessing = new();
        public PhotoSyncConfig PhotoSync = new();

        // Viewfinder capture behavior (capture runs client-side; server provides authoritative limits in multiplayer).
        internal ViewfinderConfig Viewfinder = new();

        // Timed interaction configuration (shared by client/server).
        public DevelopmentTrayInteractionConfig DevelopmentTrayInteractions = new();

        // Clamps and initializes nested config branches so runtime access stays null-safe and bounded.
        internal void ClampInPlace()
        {
            Client ??= new PhotochemistryClientConfig();
            Client.ClampInPlace();

            PlateProcessing ??= new PlateProcessingConfig();
            PlateProcessing.ClampInPlace();

            PhotoSync ??= new PhotoSyncConfig();
            PhotoSync.ClampInPlace();

            DevelopmentTrayInteractions ??= new DevelopmentTrayInteractionConfig();
            DevelopmentTrayInteractions.ClampInPlace();

            Viewfinder ??= new ViewfinderConfig();
            Viewfinder.ClampInPlace();
        }
    }

}
