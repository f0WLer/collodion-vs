using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Photochemistry.AdminTooling;
using Photochemistry.Configuration;
using Photochemistry.CameraCapture.Integration;
using Photochemistry.FieldCamera;
using Photochemistry.PhotoSync.Integration;
using Photochemistry.PlateBox;
using Photochemistry.Plates;
using Photochemistry.Tray;
using Photochemistry.Frame;

namespace Photochemistry
{
    // Shared mod bootstrap, registration, and lifecycle cleanup for both sides. Holds config, channels,
    // and runtime references shared by the client/server partials.
    //
    // Abstract so Photochemistry.Core.dll carries NO instantiable ModSystem: VintageStory refuses to load a
    // mod whose zip contains more than one DLL with a ModSystem, and every head zip bundles this Core.dll.
    // Each head supplies its own thin concrete subclass (CollodionMod, KosPhotographyMod) in its own DLL,
    // so each head zip has exactly one ModSystem DLL (the head's). The heads are mutually-exclusive installs.
    public abstract partial class PhotochemistryModSystem : ModSystem
    {
        public static PhotochemistryModSystem? ClientInstance { get; internal set; }

        public const string ConfigFileName = "photochemistry.json";
        public const string ServerPhotoIndexFileName = "photochemistry-photoindex.json";
        public const string ServerDevelopWhitelistFileName = "photochemistry-develop-whitelist.json";
        public PhotochemistryConfig Config { get; internal set; } = new PhotochemistryConfig();
        public PhotochemistryClientConfig ClientConfig { get; internal set; } = new PhotochemistryClientConfig();


        // Applies a freshly loaded/normalized config tree, keeping ClientConfig in sync.
        internal void ApplyConfig(PhotochemistryConfig cfg)
        {
            Config = cfg;
            ClientConfig = cfg.Client;
        }

        public ICoreAPI? ModApi;
        public IClientNetworkChannel? ClientChannel;
        public IServerNetworkChannel? ServerChannel;
        internal ICoreClientAPI? ClientApi;

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
            api.RegisterBlockEntityClass("BlockEntityGlassPlate", typeof(BlockEntityGlassPlate));

            RegisterBuiltInSensitizationRecipes();

            // Register Network Channel
            var channel = CameraCaptureChannelRegistration.RegisterCameraCaptureMessageTypes(api.Network.RegisterChannel("photochemistry"));

            CameraCaptureChannelRegistration.RegisterCameraCaptureConfigMessageTypes(PhotoSyncModSystemBridge.RegisterPhotoSyncMessageTypes(channel));
            AdminToolingChannelRegistration.RegisterAdminToolingMessageTypes(channel);
        }

        // Baseline collodion → iodide: pour collodion, then silver solution. Reproduces the original
        // hardcoded chain. Superset heads register their own recipes in their Start (after base.Start).
        private static void RegisterBuiltInSensitizationRecipes()
        {
            AssetLocation pourSound = new("game", "sounds/effect/water-fill");
            SensitizationRegistry.Register(new SensitizationRecipe
            {
                ChemistryId = PlateAttributes.ChemistryCollodion, // "iodide"
                SensitizedItemCode = new AssetLocation("photochemistry", "sensitizedplate"),
                Steps = new[]
                {
                    new SensitizationStep
                    {
                        Type = SensitizationInteractionType.PourLiquid,
                        Ingredient = new AssetLocation("photochemistry", "collodionportion"),
                        Amount = 40,
                        Sound = pourSound,
                        ActionLangCode = "photochemistry:heldhelp-coatglassplate"
                    },
                    new SensitizationStep
                    {
                        Type = SensitizationInteractionType.PourLiquid,
                        Ingredient = new AssetLocation("photochemistry", "silversolutionportion"),
                        Amount = 40,
                        Sound = pourSound,
                        ActionLangCode = "photochemistry:heldhelp-plate-sensitize-next"
                    }
                }
            });
        }

        // Asset-backed process profile defaults are available from this lifecycle stage.
        // Seed once here so first world join has editable ModData profile files ready.
        public override void AssetsLoaded(ICoreAPI api)
        {
            if (api is not ICoreClientAPI capi) return;

            // Load + seed the unified per-chemistry profiles (exposure physics, post-effects, presentation tone)
            // now that registered chemistries and asset-backed defaults are available; creates the file if absent.
            Exposure.ChemistryProfileRegistry.LoadAndSeed(capi.Logger);
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
                    Exposure.ChemistryProfileRegistry.Clear();

                    // These suppression flags are static and would otherwise survive a single-player
                    // world reload, leaving a stale mounted-camera position hidden from future captures.
                    CameraCapture.ViewportExposureSuppressContext.ActiveMountedCameraPos = null;
                    CameraCapture.ViewportExposureSuppressContext.IsVirtualRender = false;
                }

                if (ModApi is ICoreServerAPI sapi)
                {
                    PhotoSyncModSystemBridge.DisposeServerPhotoSyncAndMetadataRuntime(sapi);
                    FieldCameraBridge.DisposeServerFieldCamera(sapi);
                    AdminToolingBridge.DisposeServerWhitelist(sapi);
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
