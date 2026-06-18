using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Collodion.AdminTooling;
using Collodion.CameraCapture.Integration;
using Collodion.FieldCamera;
using Collodion.ImageEffects;
using Collodion.PhotoSync.Integration;
using Collodion.PlateBox;
using Collodion.Plates;
using Collodion.Plates.Blocks;
using Collodion.Tray;
using Collodion.Frame;

namespace Collodion
{
    // Shared mod bootstrap, registration, and lifecycle cleanup for both sides.
    // Holds config, channels, and runtime references shared by the client/server partials.
    public partial class CollodionModSystem : ModSystem
    {
        public static CollodionModSystem? ClientInstance { get; internal set; }

        public const string ConfigFileName = "photochemistry.json";
        public const string ServerPhotoIndexFileName = "photochemistry-photoindex.json";
        public CollodionConfig Config { get; internal set; } = new CollodionConfig();
        public CollodionClientConfig ClientConfig { get; internal set; } = new CollodionClientConfig();


        // Applies a freshly loaded/normalized config tree, keeping ClientConfig in sync.
        internal void ApplyConfig(CollodionConfig cfg)
        {
            Config = cfg;
            ClientConfig = cfg.Client;
        }

        public ICoreAPI? ModApi;
        public IClientNetworkChannel? ClientChannel;
        public IServerNetworkChannel? ServerChannel;
        internal ICoreClientAPI? ClientApi;

        private bool _effectProfilesSeeded;

        // Registers shared item/block classes and packet types used by both client and server startup paths.
        public override void Start(ICoreAPI api)
        {
            ModApi = api;

            api.RegisterItemClass("Fieldcamera", typeof(ItemFieldcamera));
            api.RegisterItemClass("GlassPlate", typeof(ItemGlassPlate));
            api.RegisterItemClass("SensitizedPlate", typeof(ItemSensitizedPlate));
            api.RegisterItemClass("PhotoPlate", typeof(ItemPhotoPlate));

            api.RegisterBlockClass("GlassPlate", typeof(BlockGlassPlate));
            api.RegisterBlockClass("DevelopmentTray", typeof(BlockDevelopmentTray));
            api.RegisterBlockClass("PlateBox", typeof(BlockPlateBox));
            api.RegisterBlockClass("BlockFrame", typeof(BlockFrame));
            api.RegisterBlockClass("BlockMountedCamera", typeof(BlockMountedCamera));
            api.RegisterBlockClass("BlockMountedCameraUpper", typeof(BlockMountedCameraUpper));
            api.RegisterBlockClass("BlockRestingCamera", typeof(BlockRestingCamera));
            api.RegisterBlockEntityClass("BlockEntityDevelopmentTray", typeof(BlockEntityDevelopmentTray));
            api.RegisterBlockEntityClass("BlockEntityPlateBox", typeof(BlockEntityPlateBox));
            api.RegisterBlockEntityClass("BlockEntityFrame", typeof(BlockEntityFrame));
            api.RegisterBlockEntityClass("BlockEntityMountedCamera", typeof(BlockEntityMountedCamera));
            api.RegisterBlockEntityClass("BlockEntityMountedCameraUpper", typeof(BlockEntityMountedCameraUpper));
            api.RegisterBlockEntityClass("BlockEntityRestingCamera", typeof(BlockEntityRestingCamera));

            // Register Network Channel
            var channel = CameraCaptureChannelRegistration.RegisterCameraCaptureMessageTypes(api.Network.RegisterChannel("photochemistry"));

            CameraCaptureChannelRegistration.RegisterCameraCaptureConfigMessageTypes(PhotoSyncModSystemBridge.RegisterPhotoSyncMessageTypes(channel));
            AdminToolingChannelRegistration.RegisterAdminToolingMessageTypes(channel);
        }

        // Asset-backed process profile defaults are available from this lifecycle stage.
        // Seed once here so first world join has editable ModData profile files ready.
        public override void AssetsLoaded(ICoreAPI api)
        {
            if (api is not ICoreClientAPI capi) return;

            _effectProfilesSeeded = ClientEffectProfileSeeder.TryPrepare(
                capi,
                _effectProfilesSeeded,
                BestEffortLogger);
        }

        // Indicates whether best-effort failure details should be emitted for diagnostics.
        internal bool IsBestEffortDebugLoggingEnabled => ClientConfig?.ShowDebugLogs == true;

        // Shared logger gate used by partials when best-effort diagnostics should honor client debug verbosity.
        internal ILogger? BestEffortLogger => IsBestEffortDebugLoggingEnabled ? (ModApi ?? ClientApi)?.Logger : null;

        // Performs final teardown for renderer, listener, sync, and singleton references.
        public override void Dispose()
        {
            try
            {
                CameraCaptureBridge.DisposeClientCameraCaptureRenderers();
                CameraCaptureBridge.DisposeClientCameraCaptureTickListeners();
                FieldCameraBridge.DisposeClientFieldCameraTickListeners();
                TrayClientEvents.DisposeClientDevelopmentTrayTickListeners();

                // The plate photo mesh cache is static and outlives a single-player world reload.
                // Its cached mesh refs / atlas texture ids belong to the GL context being torn down
                // here; without this, a relog reuses them and held photo plates render invisible.
                if (ModApi is ICoreClientAPI)
                {
                    Plates.Rendering.PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();

                    // These suppression flags are static and would otherwise survive a single-player
                    // world reload, leaving a stale mounted-camera position hidden from future captures.
                    CameraCapture.ViewportExposureSuppressContext.ActiveMountedCameraPos = null;
                    CameraCapture.ViewportExposureSuppressContext.IsVirtualRender = false;
                }

                if (ModApi is ICoreServerAPI sapi)
                {
                    PhotoSyncModSystemBridge.DisposeServerPhotoSyncAndMetadataRuntime(sapi);
                    FieldCameraBridge.DisposeServerFieldCamera(sapi);
                }
            }
            finally
            {
                CameraCaptureBridge.ClearClientCameraCaptureRuntimeReferences();
                FieldCameraBridge.ClearClientFieldCameraRuntimeReferences();
                PhotoSyncModSystemBridge.ClearPhotoSyncAndMetadataRuntimeReferences();
                ClientChannel = null;
                ServerChannel = null;

                if (ReferenceEquals(ClientInstance, this))
                {
                    ClientInstance = null;
                }

                base.Dispose();
            }
        }
    }
}
