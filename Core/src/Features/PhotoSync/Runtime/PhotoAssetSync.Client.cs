using System.Collections.Concurrent;
using Vintagestory.API.MathTools;
using Photocore.Plates.Rendering;
using Photocore.PhotoSync;

namespace Photocore.PhotoSync.Runtime
{
    public sealed partial class PhotoAssetSyncCore
    {
        // Accessed only from main thread (render callbacks and client event handlers).
        private readonly Dictionary<string, double> _clientRequestedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, IncomingAssembly> _clientIncoming = new Dictionary<string, IncomingAssembly>(StringComparer.OrdinalIgnoreCase);
        private readonly object _clientIncomingLock = new object();

        private readonly object _clientWaitLock = new object();
        private readonly Dictionary<string, HashSet<BlockPos>> _clientBlocksWaitingForPhoto = new Dictionary<string, HashSet<BlockPos>>(StringComparer.OrdinalIgnoreCase);

        // Photo ids the server has confirmed it cannot serve (a download NACK), so the render funnels show
        // the medium-specific missing-photo placeholder instead of waiting forever. Used as a set (value
        // ignored). Concurrent because block tesselation may query it off the main thread.
        private readonly ConcurrentDictionary<string, byte> _clientConfirmedMissing = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public bool ClientIsConfirmedMissing(string photoId)
        {
            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return false;
            return _clientConfirmedMissing.ContainsKey(normalizedPhotoId);
        }

        private long _clientLastStateCleanupMs;

        public void ClientOnPhotoCreated(string photoId)
        {
            if (_mod.ClientApi == null || _mod.ClientChannel == null) return;

            string path = PhotoAssetStoragePaths.GetPhotoPath(photoId);
            TryUploadPhoto(photoId, path);
        }

        public void ClientRequestPhotoIfMissing(string photoId)
        {
            if (_mod.ClientApi == null || _mod.ClientChannel == null) return;

            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return;

            string path = PhotoAssetStoragePaths.GetPhotoPath(normalizedPhotoId);
            if (File.Exists(path)) return;

            // Use a monotonic, process-wide clock so reconnecting (new World instance) doesn't break dedupe.
            long nowMs = Environment.TickCount64;
            double now = nowMs / 1000.0;
            ClientMaybeCleanupState(nowMs, now);

            if (_clientRequestedAt.TryGetValue(normalizedPhotoId, out double lastAt) && (now - lastAt) < 2.0)
            {
                return;
            }

            _clientRequestedAt[normalizedPhotoId] = now;
            _mod.ClientChannel.SendPacket(new PhotoBlobRequestPacket { PhotoId = normalizedPhotoId });
        }

        public void ClientNoteBlockWaitingForPhoto(string photoId, BlockPos pos)
        {
            if (_mod.ClientApi == null) return;

            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return;

            // Copy to avoid retaining a mutable BlockPos reference.
            BlockPos keyPos = new BlockPos(pos.X, pos.Y, pos.Z);

            lock (_clientWaitLock)
            {
                if (!_clientBlocksWaitingForPhoto.TryGetValue(normalizedPhotoId, out HashSet<BlockPos>? set) || set == null)
                {
                    set = new HashSet<BlockPos>();
                    _clientBlocksWaitingForPhoto[normalizedPhotoId] = set;
                }

                set.Add(keyPos);
            }
        }


        private void TryUploadPhoto(string photoId, string path)
        {
            if (_mod.ClientApi == null || _mod.ClientChannel == null) return;

            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return;

            if (!File.Exists(path))
            {
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch
            {
                return;
            }

            if (bytes.Length <= 0 || bytes.Length > GetMaxTransferBytes())
            {
                Log.Warn(_mod.ClientApi.Logger, $"not uploading photo {normalizedPhotoId} (size {bytes.Length} bytes exceeds limit)");
                return;
            }

            SendChunksConfigured(_mod.ClientChannel, normalizedPhotoId, bytes, isUpload: true);
        }

        public void ClientHandleChunk(PhotoBlobChunkPacket packet)
        {
            if (_mod.ClientApi == null) return;
            if (packet == null) return;
            if (packet.IsUpload) return;

            long nowMs = Environment.TickCount64;
            ClientMaybeCleanupState(nowMs, nowMs / 1000.0);

            if (!TryNormalizeAndValidateChunkPacket(packet, out string photoId)) return;

            if (!TryProcessIncomingChunk(_clientIncoming, _clientIncomingLock, photoId, packet, nowMs, out IncomingAssembly? completed)
                || completed == null)
            {
                return;
            }

            if (!LooksLikePng(completed.Buffer, completed.TotalSize))
            {
                Log.Warn(_mod.ClientApi.Logger, $"downloaded bytes for {photoId} do not look like PNG; ignoring");
                return;
            }

            if (!TryWritePhotoBytes(photoId, completed.Buffer, writeLock: null, out string? error))
            {
                Log.Warn(_mod.ClientApi.Logger, $"failed writing downloaded photo {photoId}: {error ?? "Unknown write error"}");
                return;
            }

            // A photo that previously NACK'd may have been restored/re-uploaded; clear its missing mark
            // so the render funnels stop drawing the placeholder and pull the real image.
            _clientConfirmedMissing.TryRemove(photoId, out _);

            // Kick plate render cache so the next render pulls from disk.
            // (After framed-display removal in prep for the kos-pm merge there is no separate per-item photo cache;
            // the new frame BE is expected to register its own invalidation if it adds caching.)
            PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();

            ClientMarkWaitingBlocksDirty(photoId);
        }

        private void ClientMaybeCleanupState(long nowMs, double nowSeconds)
        {
            long cleanupIntervalMs = SyncCfg?.ClientStateCleanupIntervalMs ?? 15_000;
            float requestRetainSeconds = SyncCfg?.ClientRequestRetainSeconds ?? 300f;
            long incomingStaleMs = SyncCfg?.ClientIncomingStaleMs ?? 120_000;

            if (nowMs - _clientLastStateCleanupMs < cleanupIntervalMs) return;
            _clientLastStateCleanupMs = nowMs;

            if (_clientRequestedAt.Count > 0)
            {
                List<string>? staleRequestKeys = null;
                foreach (KeyValuePair<string, double> kvp in _clientRequestedAt)
                {
                    if (nowSeconds - kvp.Value <= requestRetainSeconds) continue;
                    staleRequestKeys ??= new List<string>();
                    staleRequestKeys.Add(kvp.Key);
                }

                if (staleRequestKeys != null)
                {
                    foreach (string key in staleRequestKeys)
                    {
                        _clientRequestedAt.Remove(key);
                    }
                }
            }

            PruneStaleIncomingAssemblies(_clientIncoming, _clientIncomingLock, nowMs, incomingStaleMs);
        }

        private void ClientMarkWaitingBlocksDirty(string photoId)
        {
            if (_mod.ClientApi == null) return;

            List<BlockPos>? positions = null;
            lock (_clientWaitLock)
            {
                if (_clientBlocksWaitingForPhoto.TryGetValue(photoId, out HashSet<BlockPos>? set) && set != null && set.Count > 0)
                {
                    positions = [.. set];
                    _clientBlocksWaitingForPhoto.Remove(photoId);
                }
            }

            if (positions == null) return;

            _mod.ClientApi.Event.EnqueueMainThreadTask(() =>
            {
                foreach (BlockPos p in positions)
                {
                    try
                    {
                        _mod.ClientApi.World.BlockAccessor.MarkBlockDirty(p);
                    }
                    catch { /* intentional: best-effort non-critical path */ }
                }
            }, "photocore-photo-arrived-markdirty");
        }

        public void ClientHandleAck(PhotoBlobAckPacket packet)
        {
            if (_mod.ClientApi == null) return;
            if (packet == null) return;

            if (packet.Ok) return;

            Log.Warn(_mod.ClientApi.Logger, $"photo transfer ack failed for {packet.PhotoId}: {packet.Error}");

            // Only download NACKs mean "this photo is unavailable" — an upload NACK is about bytes we still
            // hold locally and is often transient, so it must not flag the photo as missing.
            if (packet.IsUpload) return;
            if (!TryNormalizePhotoId(packet.PhotoId, out string normalizedPhotoId)) return;

            // First time we learn a photo is gone: remember it and force a re-render so placed plates
            // (frames, trays) and held items switch to the placeholder. (Held items re-render every frame.)
            if (_clientConfirmedMissing.TryAdd(normalizedPhotoId, 0))
            {
                PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();
                ClientMarkWaitingBlocksDirty(normalizedPhotoId);
            }
        }
    }
}

