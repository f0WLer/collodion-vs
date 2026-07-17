using Vintagestory.API.Server;

using Photocore.Exposure;
using Photocore.PhotoSync;
using Photocore.PlateBox;

namespace Photocore
{
    // Server startup wiring for handlers, sync services, and authoritative config broadcast.
    // Registers packet entry points and periodic maintenance listeners used by camera/photo flows.
    public partial class PhotocoreModSystem
    {
        // Initializes server config, packet handlers, sync services, and periodic maintenance listeners.
        public override void StartServerSide(ICoreServerAPI api)
        {
            // Wire the photo-store world scope before anything below can touch a photo path (the
            // server's SavegameIdentifier is available as soon as the save is loaded, i.e. already
            // by this point -- see DESIGN-photo-store-scoping.md).
            PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => api.World?.SavegameIdentifier);

            // AssetsLoaded only seeds the client-side registry (see ModSystem.cs); the server needs its
            // own load so ConfigureServerCameraCaptureStartup's broadcast has real data to send.
            ChemistryProfileRegistry.LoadAndSeed(api.Logger);

            AdminToolingBridge.ConfigureServerOperatorToolingStartup(api);
            CameraCaptureBridge.ConfigureServerCameraCaptureStartup(api);
            FieldCameraBridge.ConfigureServerFieldCameraStartup(api);
            PlateBoxWalkSoundEvents.ConfigureServerPlateBoxWalkSound(api);

            // Server-enforced settings (PlateProcessing, tray timings) are read off this instance's
            // Config, so it needs its own reload when the ConfigLib GUI saves photocore.json. Safe
            // whether or not ConfigLib is installed.
            Configuration.ConfigLibIntegration.RegisterServer(api, this);
        }
    }
}