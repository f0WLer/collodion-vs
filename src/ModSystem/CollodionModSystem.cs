using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion
{
    public partial class CollodionModSystem : ModSystem
    {
        public static CollodionModSystem? ClientInstance { get; private set; }

        public const string ConfigFileName = "collodion.json";
        public const string ServerPhotoIndexFileName = "collodion-photoindex.json";
        public CollodionConfig Config { get; private set; } = new CollodionConfig();
        public CollodionClientConfig ClientConfig { get; private set; } = new CollodionClientConfig();
        public ProcessRegistry Processes { get; private set; } = new ProcessRegistry();

        public ICoreAPI? Api;
        public IClientNetworkChannel? ClientChannel;
        public IServerNetworkChannel? ServerChannel;
        internal ICoreClientAPI? ClientApi;
        private PhotoCaptureRenderer? CaptureRenderer;
        private ViewfinderDebugPreviewRenderer? DebugPreviewRenderer;

        internal WetplatePhotoSync? PhotoSync;

        private PhotoLastSeenIndex? serverPhotoLastSeenIndex;
        private bool serverPhotoLastSeenDirty;
        private long? serverPhotoLastSeenFlushListenerId;
        private long? serverPhotoSyncPruneListenerId;
        private long? clientDevTrayLatchTickListenerId;

        private readonly Dictionary<string, long> clientLastPhotoSeenPingMs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public override void Start(ICoreAPI api)
        {
            this.Api = api;

            Processes = new ProcessRegistry();
            api.Logger.Notification("[collodion] Loaded {0} photography process(es): {1}",
                Processes.AllProcesses.Count,
                string.Join(", ", Processes.AllProcesses.Keys));

            api.RegisterItemClass("WetplateCamera", typeof(ItemWetplateCamera));
            api.RegisterItemClass("CameraSling", typeof(ItemCameraSling));
            api.RegisterItemClass("FramedPhotograph", typeof(ItemFramedPhotograph));
            api.RegisterItemClass("GlassPlate", typeof(ItemGlassPlate));
            api.RegisterItemClass("SilveredPlate", typeof(ItemSilveredPlate));
            api.RegisterItemClass("ExposedPlate", typeof(ItemExposedPlate));
            api.RegisterItemClass("DevelopedPlate", typeof(ItemDevelopedPlate));
            api.RegisterItemClass("GenericPlate", typeof(ItemGenericPlate));
            api.RegisterItemClass("FinishedPhotoPlate", typeof(ItemFinishedPhotoPlate));

            api.RegisterBlockClass("GlassPlate", typeof(BlockGlassPlate));
            api.RegisterBlockClass("BlockFramedPhotograph", typeof(BlockFramedPhotograph));
            api.RegisterBlockClass("DevelopmentTray", typeof(BlockDevelopmentTray));
            api.RegisterBlockClass("WallMountedCameraSling", typeof(BlockWallMountedCameraSling));
            api.RegisterBlockClass("PlateBox", typeof(BlockPlateBox));
            api.RegisterBlockEntityClass("BlockEntityPhotograph", typeof(BlockEntityPhotograph));
            api.RegisterBlockEntityClass("BlockEntityDevelopmentTray", typeof(BlockEntityDevelopmentTray));
            api.RegisterBlockEntityClass("BlockEntityWallMountedCameraSling", typeof(BlockEntityWallMountedCameraSling));
            api.RegisterBlockEntityClass("BlockEntityPlateBox", typeof(BlockEntityPlateBox));
            
            // Register Network Channel
            api.Network.RegisterChannel("collodion")
                .RegisterMessageType(typeof(PhotoTakenPacket))
                .RegisterMessageType(typeof(CameraLoadPlatePacket))
                .RegisterMessageType(typeof(CameraSlingTogglePacket))
                .RegisterMessageType(typeof(CameraAttachSlingPacket))
                .RegisterMessageType(typeof(PhotoBlobRequestPacket))
                .RegisterMessageType(typeof(PhotoBlobChunkPacket))
                .RegisterMessageType(typeof(PhotoBlobAckPacket))
                .RegisterMessageType(typeof(PhotoCaptionSetPacket))
                .RegisterMessageType(typeof(PhotoSeenPacket))
                .RegisterMessageType(typeof(PhotoCaptureConfigRequestPacket))
                .RegisterMessageType(typeof(PhotoCaptureConfigPacket))
                .RegisterMessageType(typeof(SetPlateProcessPacket));
        }

        private static void TryRun(Action action)
        {
            try { action(); } catch { /* intentional: best-effort non-critical path */ }
        }

        private void DisposeClientRenderers()
        {
            if (ClientApi == null) return;

            if (CaptureRenderer != null)
            {
                TryRun(() => ClientApi.Event.UnregisterRenderer(CaptureRenderer, EnumRenderStage.AfterBlit));
                TryRun(() => CaptureRenderer.Dispose());
            }

            if (DebugPreviewRenderer != null)
            {
                TryRun(() => ClientApi.Event.UnregisterRenderer(DebugPreviewRenderer, EnumRenderStage.Ortho));
                TryRun(() => DebugPreviewRenderer.Dispose());
            }
        }

        private void DisposeClientTickListeners()
        {
            if (ClientApi == null) return;

            if (viewfinderTickListenerId > 0)
            {
                TryRun(() => ClientApi.Event.UnregisterGameTickListener(viewfinderTickListenerId));
                viewfinderTickListenerId = 0;
            }

            if (clientDevTrayLatchTickListenerId.HasValue && clientDevTrayLatchTickListenerId.Value > 0)
            {
                long id = clientDevTrayLatchTickListenerId.Value;
                TryRun(() => ClientApi.Event.UnregisterGameTickListener(id));
                clientDevTrayLatchTickListenerId = null;
            }

            if (clientCaptureConfigRetryTickListenerId.HasValue && clientCaptureConfigRetryTickListenerId.Value > 0)
            {
                long id = clientCaptureConfigRetryTickListenerId.Value;
                TryRun(() => ClientApi.Event.UnregisterGameTickListener(id));
                clientCaptureConfigRetryTickListenerId = null;
            }
        }

        private void DisposeServerTickListenersAndFlush(ICoreServerAPI sapi)
        {
            if (serverPhotoLastSeenFlushListenerId.HasValue && serverPhotoLastSeenFlushListenerId.Value > 0)
            {
                long id = serverPhotoLastSeenFlushListenerId.Value;
                TryRun(() => sapi.Event.UnregisterGameTickListener(id));
                serverPhotoLastSeenFlushListenerId = null;
            }

            if (serverPhotoSyncPruneListenerId.HasValue && serverPhotoSyncPruneListenerId.Value > 0)
            {
                long id = serverPhotoSyncPruneListenerId.Value;
                TryRun(() => sapi.Event.UnregisterGameTickListener(id));
                serverPhotoSyncPruneListenerId = null;
            }

            TryRun(() => ServerMaybeFlushPhotoLastSeenIndex(sapi));
        }

        public override void Dispose()
        {
            try
            {
                DisposeClientRenderers();
                DisposeClientTickListeners();

                if (Api is ICoreServerAPI sapi)
                {
                    DisposeServerTickListenersAndFlush(sapi);
                }
            }
            finally
            {
                CaptureRenderer = null;
                DebugPreviewRenderer = null;
                PhotoSync = null;
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
