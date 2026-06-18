using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Photochemistry.PhotoSync.Contracts;
using Photochemistry.PhotoSync.Storage;

namespace Photochemistry.PhotoSync.Runtime
{
    // Server-side photo request/upload handling and incoming-assembly cleanup.
    // Reassembles chunks, validates png payloads, and persists synced photo bytes.
    public sealed partial class PhotoAssetSyncCore
    {
        internal PlayerNetworkThrottle ServerRequestThrottle
            => _serverRequestThrottle ??= new PlayerNetworkThrottle(RequestPermitsPerMinute, RequestBurstCapacity);
        // server-side: in-progress uploads by (playerUid|photoId)
        private readonly Dictionary<string, IncomingAssembly> _serverIncoming = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _serverIncomingLock = new();
        private readonly object _writeLock = new();

        // Single in-mod tuning point. The only knob exposed to admin config is
        // ServerMaxOpenUploadsPerPlayer (the value most likely to need server-specific tuning).
        private const int RequestPermitsPerMinute = 60;
        private const int RequestBurstCapacity = 8;
        private const long ExpectedUploadTtlMs = 60_000;

        private PlayerNetworkThrottle? _serverRequestThrottle;
        private ServerExpectedUploads? _serverExpectedUploads;

        internal ServerExpectedUploads ExpectedUploads
            => _serverExpectedUploads ??= new ServerExpectedUploads(ExpectedUploadTtlMs);

        // Called by the camera authority after a PhotoTakenPacket has been authoritatively accepted.
        // Marks this (player, photoId) pair as eligible to upload bytes within the configured TTL.
        public void RegisterExpectedUpload(string playerUid, string photoId)
        {
            ExpectedUploads.Register(playerUid, photoId, Environment.TickCount64);
        }

        private const int DefaultChunkSize = 24 * 1024;
        private const int DefaultMaxBytes = 2 * 1024 * 1024; // plenty for configured capture sizes

        // Gets chunk size.
        private int GetChunkSizeBytes()
        {
            int size = SyncCfg?.ChunkSizeBytes ?? DefaultChunkSize;
            if (size < 1024) size = 1024;
            return size;
        }

        // Reads the maximum allowed upload/download size so sync logic can reject oversized transfers early.
        private int GetMaxTransferBytes()
        {
            int max = SyncCfg?.MaxTransferBytes ?? DefaultMaxBytes;
            if (max < 16 * 1024) max = 16 * 1024;
            return max;
        }

        // Minimal cleanup so abandoned uploads (disconnects mid-transfer) don't accumulate.
        private long _serverLastPruneMs;

        // Small server tick hook that lets the mod system periodically prune abandoned upload assemblies.
        public void ServerPruneTick(long nowMs) => ServerMaybePruneIncoming(nowMs);

        // Sends one normalized transfer acknowledgement back to the requesting player.
        private void SendServerTransferAck(IServerPlayer toPlayer, string photoId, bool ok, string? error = null)
        {
            if (_mod.ServerChannel == null) return;

            _mod.ServerChannel.SendPacket(new PhotoBlobAckPacket
            {
                PhotoId = photoId,
                Ok = ok,
                Error = ok ? string.Empty : (error ?? "Photo transfer failed")
            }, toPlayer);
        }

        // Loads one persisted photo for download and validates transfer-size constraints.
        private bool TryLoadPhotoBytesForDownload(string photoId, out byte[]? bytes, out string? error)
        {
            bytes = null;
            error = null;

            string path = PhotoAssetStoragePaths.GetPhotoPath(photoId);
            if (!File.Exists(path))
            {
                error = "Photo not present on server";
                return false;
            }

            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (bytes.Length <= 0 || bytes.Length > GetMaxTransferBytes())
            {
                bytes = null;
                error = "Photo too large";
                return false;
            }

            return true;
        }

        // Applies server upload completion policy (png gate, disk write, seen touch, ack response).
        // The png-gate runs on the calling thread; the actual file write is dispatched to the thread pool
        // so it does not block the packet handler / main server thread.
        private void TryPersistUploadedPhoto(IServerPlayer fromPlayer, string photoId, IncomingAssembly completed)
        {
            if (!LooksLikePng(completed.Buffer, completed.TotalSize))
            {
                SendServerTransferAck(fromPlayer, photoId, ok: false, "Invalid PNG");
                return;
            }

            byte[] buffer = completed.Buffer;
            object writeLock = _writeLock;
            var sapi = _mod.ModApi as ICoreServerAPI;

            TyronThreadPool.QueueTask(() =>
            {
                bool ok = TryWritePhotoBytes(photoId, buffer, writeLock, out string? error);

                // Hop back to the main server thread so SendPacket / SeenTouch run on the expected thread.
                if (sapi != null)
                {
                    sapi.Event.EnqueueMainThreadTask(() =>
                    {
                        if (ok)
                        {
                            _mod.PhotoSyncModSystemBridge.ServerTouchPhotoSeen(photoId);
                            SendServerTransferAck(fromPlayer, photoId, ok: true);
                        }
                        else
                        {
                            SendServerTransferAck(fromPlayer, photoId, ok: false, error ?? "Photo write failed");
                        }
                    }, "photochemistry:UploadAck");
                }
            }, "photochemistry:UploadWrite");
        }

        // Removes stale in-progress uploads so disconnects or failed transfers do not leak memory indefinitely.
        private void ServerMaybePruneIncoming(long nowMs)
        {
            int pruneIntervalMs = SyncCfg?.ServerPruneIntervalMs ?? 30_000;
            int uploadStaleMs = SyncCfg?.ServerUploadStaleMs ?? 120_000;

            lock (_serverIncomingLock)
            {
                if (_serverIncoming.Count == 0) return;
                if (_serverLastPruneMs != 0 && (nowMs - _serverLastPruneMs) < pruneIntervalMs) return;
                _serverLastPruneMs = nowMs;
            }

            PruneStaleIncomingAssemblies(_serverIncoming, _serverIncomingLock, nowMs, uploadStaleMs);
        }

        // Handles client download requests by reading the server-side png and streaming it back in chunks.
        // The disk read is dispatched to the thread pool so it does not block the packet handler thread;
        // chunk send and seen-touch run back on the main server thread.
        public void ServerHandleRequest(IServerPlayer fromPlayer, PhotoBlobRequestPacket packet)
        {
            if (_mod.ModApi == null || _mod.ServerChannel == null) return;
            if (fromPlayer == null || packet == null) return;

            // Per-player rate limit: protect disk + bandwidth from request floods.
            if (!ServerRequestThrottle.TryConsume(fromPlayer.PlayerUID, "req", Environment.TickCount64)) return;

            if (!TryNormalizePhotoId(packet.PhotoId, out string photoId)) return;

            // Path existence and size limit can both be checked off-thread inside TryLoadPhotoBytesForDownload.
            var sapi = _mod.ModApi as ICoreServerAPI;
            if (sapi == null) return;

            TyronThreadPool.QueueTask(() =>
            {
                bool ok = TryLoadPhotoBytesForDownload(photoId, out byte[]? bytes, out string? error);

                sapi.Event.EnqueueMainThreadTask(() =>
                {
                    if (!ok || bytes == null)
                    {
                        SendServerTransferAck(fromPlayer, photoId, ok: false, error);
                        return;
                    }

                    if (_mod.ServerChannel == null) return;
                    _mod.PhotoSyncModSystemBridge.ServerTouchPhotoSeen(photoId);
                    SendChunksConfigured(_mod.ServerChannel, fromPlayer, photoId, bytes, isUpload: false);
                }, "photochemistry:DownloadDispatch");
            }, "photochemistry:DownloadRead");
        }

        // Reassembles uploaded chunks from one client, validates the png, and persists it to the server photo store.
        public void ServerHandleChunk(IServerPlayer fromPlayer, PhotoBlobChunkPacket packet)
        {
            if (_mod.ModApi == null || _mod.ServerChannel == null) return;
            if (fromPlayer == null || packet == null) return;
            if (!packet.IsUpload) return; // ignore downloads on server

            long nowMs = Environment.TickCount64;
            ServerMaybePruneIncoming(nowMs);

            if (!TryNormalizeAndValidateChunkPacket(packet, out string photoId)) return;

            string playerUid = fromPlayer.PlayerUID;

            // Photo-id authorization: client must have legitimately captured this photo to upload bytes for it.
            if (!ExpectedUploads.IsExpected(playerUid, photoId, nowMs))
            {
                SendServerTransferAck(fromPlayer, photoId, ok: false, "Upload not authorized for this photo id");
                return;
            }

            // Isolate uploads per player so equal photo ids from different clients cannot collide in assembly state.
            string key = playerUid + "|" + photoId;

            // First-chunk gating: enforce per-player concurrent upload cap.
            bool isNewAssembly;
            lock (_serverIncomingLock)
            {
                isNewAssembly = !_serverIncoming.ContainsKey(key);
            }
            if (isNewAssembly)
            {
                int maxOpen = SyncCfg?.ServerMaxOpenUploadsPerPlayer ?? 2;
                if (!ExpectedUploads.TryBeginUpload(playerUid, maxOpen))
                {
                    SendServerTransferAck(fromPlayer, photoId, ok: false, "Too many concurrent uploads");
                    return;
                }
            }

            if (!TryProcessIncomingChunk(_serverIncoming, _serverIncomingLock, key, packet, nowMs, out IncomingAssembly? completed)
                || completed == null)
            {
                return;
            }

            // Completed: release accounting and consume the expected-upload entry (single-shot).
            // Note: if a player abandons mid-upload, their cap slot stays held until the assembly
            // ages out (~2 minutes). That self-locks only the offending player; not a server-wide concern.
            ExpectedUploads.EndUpload(playerUid);
            ExpectedUploads.Consume(playerUid, photoId);

            TryPersistUploadedPhoto(fromPlayer, photoId, completed);
        }

    }
}
