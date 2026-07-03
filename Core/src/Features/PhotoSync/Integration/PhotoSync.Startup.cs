using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Photocore.PhotoMetadata;
using Photocore.PhotoSync;
using Photocore.PhotoSync.Runtime;
using Photocore.PhotoSync.Store;

namespace Photocore.PhotoSync.Integration
{
    internal sealed partial class PhotoSyncModSystemBridge
    {
        private readonly PhotocoreModSystem _owner;
        internal PhotoAssetSyncCore? Runtime;
        private IPhotoStore? _photoStore;

        internal PhotoSyncModSystemBridge(PhotocoreModSystem owner)
        {
            _owner = owner;
        }

        // Ensures feature runtime exists before handler registration or side-specific startup uses it.
        private PhotoAssetSyncCore GetOrCreatePhotoSyncRuntime()
        {
            return Runtime ??= new PhotoAssetSyncCore(_owner);
        }

        // Public IPhotoStore entry point (see PhotocoreModSystem.PhotoStore). The server is always
        // authoritative so it reads local disk directly; the client routes through the sync runtime
        // since its disk copy is only a cache. Side selection needs ModApi, so resolving before Start
        // has run must fail loudly — memoizing a guess would permanently wire the wrong side.
        internal IPhotoStore PhotoStore
        {
            get
            {
                if (_photoStore != null) return _photoStore;
                if (_owner.ModApi == null)
                {
                    throw new InvalidOperationException(
                        "PhotoStore is not available before mod startup; resolve it after PhotocoreModSystem.Start has run.");
                }
                return _photoStore = _owner.ModApi is ICoreServerAPI
                    ? new ServerPhotoStore()
                    : new ClientPhotoStore(GetOrCreatePhotoSyncRuntime());
            }
        }

        // Registers PhotoSync packet DTOs on the shared channel, preserving wire-order invariants.
        internal static INetworkChannel RegisterPhotoSyncMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(PhotoBlobRequestPacket))
                .RegisterMessageType(typeof(PhotoBlobChunkPacket))
                .RegisterMessageType(typeof(PhotoBlobAckPacket))
                .RegisterMessageType(typeof(PhotoCaptionSetPacket))
                .RegisterMessageType(typeof(PhotoSeenPacket));
        }

        internal void ConfigureClientPhotoSyncStartup()
        {
            GetOrCreatePhotoSyncRuntime();
        }

        internal void ConfigureClientPhotoSyncTransferChannelHandlers()
        {
            if (_owner.ClientChannel == null) return;

            _owner.ClientChannel
                .SetMessageHandler<PhotoBlobChunkPacket>(HandleClientPhotoSyncChunkPacket)
                .SetMessageHandler<PhotoBlobAckPacket>(HandleClientPhotoSyncAckPacket);
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleClientPhotoSyncChunkPacket(PhotoBlobChunkPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ClientHandleChunk(packet);
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleClientPhotoSyncAckPacket(PhotoBlobAckPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ClientHandleAck(packet);
        }

        internal void ConfigureServerPhotoSyncRuntime(ICoreServerAPI api)
        {
            PhotoAssetSyncCore runtime = GetOrCreatePhotoSyncRuntime();
            _serverPhotoSyncPruneListenerId = api.Event.RegisterGameTickListener(_ => runtime.ServerPruneTick(Environment.TickCount64), 10_000);

            _serverPhotoSeenService = ServerPhotoSeenService.LoadOrCreate(api, PhotocoreModSystem.ServerPhotoIndexFileName);
            _serverPhotoLastSeenFlushListenerId = api.Event.RegisterGameTickListener(_ => _serverPhotoSeenService?.TryFlush(api), 10_000);
        }

        internal void ConfigureServerPhotoSyncTransferChannelHandlers()
        {
            if (_owner.ServerChannel == null) return;

            _owner.ServerChannel
                .SetMessageHandler<PhotoBlobRequestPacket>(HandleServerPhotoSyncRequestPacket)
                .SetMessageHandler<PhotoBlobChunkPacket>(HandleServerPhotoSyncChunkPacket);
        }

        internal void ConfigureServerPhotoSeenChannelHandler()
        {
            if (_owner.ServerChannel == null) return;

            _owner.ServerChannel
                .SetMessageHandler<PhotoSeenPacket>((player, packet) =>
                {
                    if (packet?.PhotoId != null)
                        ServerTouchPhotoSeen(packet.PhotoId);
                });
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleServerPhotoSyncRequestPacket(IServerPlayer player, PhotoBlobRequestPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ServerHandleRequest(player, packet);
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleServerPhotoSyncChunkPacket(IServerPlayer player, PhotoBlobChunkPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ServerHandleChunk(player, packet);
        }

        internal void DisposeServerPhotoSyncAndMetadataRuntime(ICoreServerAPI sapi)
        {
            if (_serverPhotoLastSeenFlushListenerId.HasValue && _serverPhotoLastSeenFlushListenerId.Value > 0)
            {
                long id = _serverPhotoLastSeenFlushListenerId.Value;
                BestEffort.Try(_owner.BestEffortLogger, "unregister server photo last-seen flush listener", () => sapi.Event.UnregisterGameTickListener(id));
                _serverPhotoLastSeenFlushListenerId = null;
            }

            if (_serverPhotoSyncPruneListenerId.HasValue && _serverPhotoSyncPruneListenerId.Value > 0)
            {
                long id = _serverPhotoSyncPruneListenerId.Value;
                BestEffort.Try(_owner.BestEffortLogger, "unregister server photo sync prune listener", () => sapi.Event.UnregisterGameTickListener(id));
                _serverPhotoSyncPruneListenerId = null;
            }

            BestEffort.Try(_owner.BestEffortLogger, "flush server photo last-seen index on dispose", () => _serverPhotoSeenService?.TryFlush(sapi));
        }

        // Clears feature-owned sync/metadata runtime references during mod shutdown.
        internal void ClearPhotoSyncAndMetadataRuntimeReferences()
        {
            // Cancel outstanding IPhotoStore waiters before dropping the runtime, or third-party
            // awaits would hang forever across a world unload.
            Runtime?.ClientAbandonAllPhotoWaiters();

            Runtime = null;
            _photoStore = null;
            _serverPhotoSeenService = null;
        }
    }
}
