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

        public ICoreAPI? Api;
        public IClientNetworkChannel? ClientChannel;
        public IServerNetworkChannel? ServerChannel;
        internal ICoreClientAPI? ClientApi;
        private PhotoCaptureRenderer? CaptureRenderer;

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
                .RegisterMessageType(typeof(PhotoCaptureConfigPacket));
        }

        public override void Dispose()
        {
            try
            {
                if (ClientApi != null)
                {
                    if (CaptureRenderer != null)
                    {
                        try
                        {
                            ClientApi.Event.UnregisterRenderer(CaptureRenderer, EnumRenderStage.AfterBlit);
                        }
                        catch { /* intentional: best-effort non-critical path */ }

                        try
                        {
                            CaptureRenderer.Dispose();
                        }
                        catch { /* intentional: best-effort non-critical path */ }
                    }

                    if (viewfinderTickListenerId > 0)
                    {
                        try { ClientApi.Event.UnregisterGameTickListener(viewfinderTickListenerId); } catch { /* intentional: best-effort non-critical path */ }
                        viewfinderTickListenerId = 0;
                    }

                    if (clientDevTrayLatchTickListenerId.HasValue && clientDevTrayLatchTickListenerId.Value > 0)
                    {
                        try { ClientApi.Event.UnregisterGameTickListener(clientDevTrayLatchTickListenerId.Value); } catch { /* intentional: best-effort non-critical path */ }
                        clientDevTrayLatchTickListenerId = null;
                    }

                    if (clientCaptureConfigRetryTickListenerId.HasValue && clientCaptureConfigRetryTickListenerId.Value > 0)
                    {
                        try { ClientApi.Event.UnregisterGameTickListener(clientCaptureConfigRetryTickListenerId.Value); } catch { /* intentional: best-effort non-critical path */ }
                        clientCaptureConfigRetryTickListenerId = null;
                    }
                }

                if (Api is ICoreServerAPI sapi)
                {
                    if (serverPhotoLastSeenFlushListenerId.HasValue && serverPhotoLastSeenFlushListenerId.Value > 0)
                    {
                        try { sapi.Event.UnregisterGameTickListener(serverPhotoLastSeenFlushListenerId.Value); } catch { /* intentional: best-effort non-critical path */ }
                        serverPhotoLastSeenFlushListenerId = null;
                    }

                    if (serverPhotoSyncPruneListenerId.HasValue && serverPhotoSyncPruneListenerId.Value > 0)
                    {
                        try { sapi.Event.UnregisterGameTickListener(serverPhotoSyncPruneListenerId.Value); } catch { /* intentional: best-effort non-critical path */ }
                        serverPhotoSyncPruneListenerId = null;
                    }

                    try { ServerMaybeFlushPhotoLastSeenIndex(sapi); } catch { /* intentional: best-effort non-critical path */ }
                }
            }
            finally
            {
                CaptureRenderer = null;
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
