using Vintagestory.API.Server;

using Photocore.Exposure;

namespace Photocore
{
    // Server startup wiring for handlers, sync services, and authoritative config broadcast.
    // Registers packet entry points and periodic maintenance listeners used by camera/photo flows.
    public partial class PhotocoreModSystem
    {
        // Initializes server config, packet handlers, sync services, and periodic maintenance listeners.
        public override void StartServerSide(ICoreServerAPI api)
        {
            // AssetsLoaded only seeds the client-side registry (see ModSystem.cs); the server needs its
            // own load so ConfigureServerCameraCaptureStartup's broadcast has real data to send.
            ChemistryProfileRegistry.LoadAndSeed(api.Logger);

            AdminToolingBridge.ConfigureServerOperatorToolingStartup(api);
            CameraCaptureBridge.ConfigureServerCameraCaptureStartup(api);
            FieldCameraBridge.ConfigureServerFieldCameraStartup(api);
        }
    }
}