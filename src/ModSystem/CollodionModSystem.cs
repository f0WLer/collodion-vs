using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoTakenPacket
    {
        [ProtoMember(1)]
        public string PhotoId { get; set; } = string.Empty;

        [ProtoMember(2)]
        public float HoldStillSeconds { get; set; }

        [ProtoMember(3)]
        public float HoldStillMovement { get; set; }
    }

    [ProtoContract]
    public class CameraLoadPlatePacket
    {
        [ProtoMember(1)]
        public bool Load { get; set; }
    }

    [ProtoContract]
    public class CameraSlingTogglePacket
    {
        [ProtoMember(1)]
        public bool TryWallMount { get; set; }

        [ProtoMember(2)]
        public int TargetX { get; set; }

        [ProtoMember(3)]
        public int TargetY { get; set; }

        [ProtoMember(4)]
        public int TargetZ { get; set; }

        [ProtoMember(5)]
        public string TargetFaceCode { get; set; } = string.Empty;
    }

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
            api.RegisterBlockEntityClass("BlockEntityPhotograph", typeof(BlockEntityPhotograph));
            api.RegisterBlockEntityClass("BlockEntityDevelopmentTray", typeof(BlockEntityDevelopmentTray));
            api.RegisterBlockEntityClass("BlockEntityWallMountedCameraSling", typeof(BlockEntityWallMountedCameraSling));
            
            // Register Network Channel
            api.Network.RegisterChannel("collodion")
                .RegisterMessageType(typeof(PhotoTakenPacket))
                .RegisterMessageType(typeof(CameraLoadPlatePacket))
                .RegisterMessageType(typeof(CameraSlingTogglePacket))
                .RegisterMessageType(typeof(PhotoBlobRequestPacket))
                .RegisterMessageType(typeof(PhotoBlobChunkPacket))
                .RegisterMessageType(typeof(PhotoBlobAckPacket))
                .RegisterMessageType(typeof(PhotoCaptionSetPacket))
                .RegisterMessageType(typeof(PhotoSeenPacket));
        }
    }
}