using Vintagestory.API.Client;
using Photocore.Configuration;

namespace Photocore
{
    // Client startup wiring for channels, renderers, commands, and tick listeners.
    // Keeps client-only bootstrap and config persistence separate from server startup.
    public partial class PhotocoreModSystem
    {
        // Client startup wires networking, renderers, hotkeys, config, and the viewfinder polling loop.
        public override void StartClientSide(ICoreClientAPI api)
        {
            AdminToolingBridge.ConfigureClientOperatorToolingStartup(api);
            PhotoSyncModSystemBridge.ConfigureClientPhotoSyncStartup();
            CameraCaptureBridge.ConfigureClientCameraCaptureStartup(api);
            FieldCameraBridge.ConfigureClientFieldCameraStartup(api);
            TrayClientEvents.ConfigureClientDevelopmentTrayInputListeners(api);

            // Stop held translucent plates from depth-culling entities/frames behind them.
            Plates.Rendering.HeldPlateDepthPatch.Apply(api);

            // Safe with or without ConfigLib installed: this only subscribes to an event bus name, and
            // nothing pushes that event when ConfigLib is absent.
            ConfigLibIntegration.RegisterClient(api, this);
        }

        // Lazily ensures the full client config tree is available before UI or render code reads from it.
        internal PhotocoreConfig GetOrLoadClientConfig(ICoreClientAPI capi)
        {
            if (Config == null)
            {
                Config = ConfigLifecycle.LoadOrCreate(capi, ConfigFileName);
            }
            else
            {
                Config = ConfigLifecycle.EnsureNormalized(Config);
            }

            ClientConfig = Config.Client;
            return Config;
        }

        // No "save the in-memory client config to disk" helper on purpose. While connected to a server,
        // Config carries that server's authoritative values (see ServerConfigOverridePacket), so writing
        // it back to photocore.json would persist another server's settings into this player's own file
        // and carry them into their singleplayer worlds. The only writer is ConfigLifecycle.LoadOrCreate,
        // which normalizes what it just read from disk and applies overrides afterwards, in memory only.
    }
}

