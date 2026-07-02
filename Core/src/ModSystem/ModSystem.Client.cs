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

            #if CONFIGLIB_ENABLED
                if (ConfigLibIntegration.IsPresent(api)) ConfigLibIntegration.Register(api, this);
            #endif
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

        // Persists the current client config back to disk after clamping it into a safe range.
        internal void SaveClientConfig(ICoreClientAPI capi)
        {
            if (Config == null) return;

            ConfigLifecycle.TryStoreNormalized(capi, ConfigFileName, Config);
            Config = ConfigLifecycle.EnsureNormalized(Config);
            ClientConfig = Config.Client;
        }

    }
}

