using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public sealed partial class WetplatePhotoSync
    {
        // client-side: dedupe requests
        private readonly Dictionary<string, double> clientRequestedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // client-side: in-progress download assemblies by photoId
        private readonly Dictionary<string, IncomingAssembly> clientIncoming = new Dictionary<string, IncomingAssembly>(StringComparer.OrdinalIgnoreCase);
        private readonly object clientIncomingLock = new object();

        // client-side: mounted blocks waiting for a specific photoId
        private readonly object clientWaitLock = new object();
        private readonly Dictionary<string, HashSet<BlockPos>> clientBlocksWaitingForPhoto = new Dictionary<string, HashSet<BlockPos>>(StringComparer.OrdinalIgnoreCase);

        private long clientLastStateCleanupMs;

        public void ClientOnPhotoCreated(string photoId)
        {
            if (mod.ClientApi == null || mod.ClientChannel == null) return;

            // Upload to server (best effort).
            string path = GetPhotoPath(photoId);
            TryUploadPhoto(photoId, path);
        }

        public void ClientRequestPhotoIfMissing(string photoId)
        {
            if (mod.ClientApi == null || mod.ClientChannel == null) return;

            photoId = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoId)) return;

            string path = GetPhotoPath(photoId);
            if (File.Exists(path)) return;

            // Use a monotonic, process-wide clock so reconnecting (new World instance) doesn't break dedupe.
            long nowMs = Environment.TickCount64;
            double now = nowMs / 1000.0;
            ClientMaybeCleanupState(nowMs, now);

            if (clientRequestedAt.TryGetValue(photoId, out double lastAt) && (now - lastAt) < 2.0)
            {
                return;
            }

            clientRequestedAt[photoId] = now;
            mod.ClientChannel.SendPacket(new PhotoBlobRequestPacket { PhotoId = photoId });
        }

        public void ClientNoteBlockWaitingForPhoto(string photoId, BlockPos pos)
        {
            if (mod.ClientApi == null) return;

            photoId = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoId)) return;

            // Copy to avoid retaining a mutable BlockPos reference.
            BlockPos keyPos = new BlockPos(pos.X, pos.Y, pos.Z);

            lock (clientWaitLock)
            {
                if (!clientBlocksWaitingForPhoto.TryGetValue(photoId, out HashSet<BlockPos>? set) || set == null)
                {
                    set = new HashSet<BlockPos>();
                    clientBlocksWaitingForPhoto[photoId] = set;
                }

                set.Add(keyPos);
            }
        }


        private void TryUploadPhoto(string photoId, string path)
        {
            if (mod.ClientApi == null || mod.ClientChannel == null) return;

            photoId = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoId)) return;

            if (!File.Exists(path))
            {
                // Nothing to upload.
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
                mod.ClientApi.Logger.Warning($"Wetplate: not uploading photo {photoId} (size {bytes.Length} bytes exceeds limit)");
                return;
            }

            SendChunksConfigured(mod.ClientChannel, photoId, bytes, isUpload: true);
        }

        public void ClientHandleChunk(PhotoBlobChunkPacket packet)
        {
            if (mod.ClientApi == null) return;
            if (packet == null) return;
            if (packet.IsUpload) return; // ignore uploads on client

            long nowMs = Environment.TickCount64;
            ClientMaybeCleanupState(nowMs, nowMs / 1000.0);

            string photoId = NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrEmpty(photoId)) return;

            if (packet.TotalSize <= 0 || packet.TotalSize > GetMaxTransferBytes()) return;
            if (packet.ChunkCount <= 0 || packet.ChunkCount > 4096) return;
            if (packet.ChunkIndex < 0 || packet.ChunkIndex >= packet.ChunkCount) return;
            if (packet.Data == null) return;

            IncomingAssembly? completed = null;
            lock (clientIncomingLock)
            {
                if (!clientIncoming.TryGetValue(photoId, out IncomingAssembly? asm) || asm == null || asm.TotalSize != packet.TotalSize || asm.ChunkCount != packet.ChunkCount)
                {
                    asm = new IncomingAssembly(packet.TotalSize, packet.ChunkCount);
                    clientIncoming[photoId] = asm;
                }

                if (asm == null) return;
                asm.LastTouchedMs = nowMs;

                if (asm.Received[packet.ChunkIndex]) return;

                int offset = packet.ChunkIndex * GetChunkSizeBytes();
                int copyLen = Math.Min(packet.Data.Length, asm.TotalSize - offset);
                if (copyLen <= 0) return;

                Buffer.BlockCopy(packet.Data, 0, asm.Buffer, offset, copyLen);
                asm.Received[packet.ChunkIndex] = true;
                asm.ReceivedChunks++;

                if (asm.ReceivedChunks < asm.ChunkCount) return;

                clientIncoming.Remove(photoId);
                completed = asm;
            }

            if (completed == null) return;

            // Basic PNG signature check
            if (completed.TotalSize < 8 || completed.Buffer[0] != 0x89 || completed.Buffer[1] != 0x50 || completed.Buffer[2] != 0x4E || completed.Buffer[3] != 0x47)
            {
                mod.ClientApi.Logger.Warning($"Wetplate: downloaded bytes for {photoId} do not look like PNG; ignoring");
                return;
            }

            try
            {
                string outPath = GetPhotoPath(photoId);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, completed.Buffer);

                // Kick item render cache so the next render pulls from disk.
                ItemFramedPhotograph.ClearClientRenderCacheAndBumpVersion();

                // Nudge any mounted-photo blocks that were waiting on this file.
                ClientMarkWaitingBlocksDirty(photoId);
            }
            catch (Exception ex)
            {
                mod.ClientApi.Logger.Warning($"Wetplate: failed writing downloaded photo {photoId}: {ex.Message}");
            }
        }

        private void ClientMaybeCleanupState(long nowMs, double nowSeconds)
        {
            long cleanupIntervalMs = SyncCfg?.ClientStateCleanupIntervalMs ?? 15_000;
            float requestRetainSeconds = SyncCfg?.ClientRequestRetainSeconds ?? 300f;
            long incomingStaleMs = SyncCfg?.ClientIncomingStaleMs ?? 120_000;

            if (nowMs - clientLastStateCleanupMs < cleanupIntervalMs) return;
            clientLastStateCleanupMs = nowMs;

            if (clientRequestedAt.Count > 0)
            {
                List<string>? staleRequestKeys = null;
                foreach (KeyValuePair<string, double> kvp in clientRequestedAt)
                {
                    if (nowSeconds - kvp.Value <= requestRetainSeconds) continue;
                    staleRequestKeys ??= new List<string>();
                    staleRequestKeys.Add(kvp.Key);
                }

                if (staleRequestKeys != null)
                {
                    foreach (string key in staleRequestKeys)
                    {
                        clientRequestedAt.Remove(key);
                    }
                }
            }

            lock (clientIncomingLock)
            {
                if (clientIncoming.Count > 0)
                {
                    List<string>? staleIncomingKeys = null;
                    foreach (KeyValuePair<string, IncomingAssembly> kvp in clientIncoming)
                    {
                        IncomingAssembly asm = kvp.Value;
                        if (asm == null) continue;
                        if (nowMs - asm.LastTouchedMs <= incomingStaleMs) continue;

                        staleIncomingKeys ??= new List<string>();
                        staleIncomingKeys.Add(kvp.Key);
                    }

                    if (staleIncomingKeys != null)
                    {
                        foreach (string key in staleIncomingKeys)
                        {
                            clientIncoming.Remove(key);
                        }
                    }
                }
            }
        }

        private void ClientMarkWaitingBlocksDirty(string photoId)
        {
            if (mod.ClientApi == null) return;

            List<BlockPos>? positions = null;
            lock (clientWaitLock)
            {
                if (clientBlocksWaitingForPhoto.TryGetValue(photoId, out HashSet<BlockPos>? set) && set != null && set.Count > 0)
                {
                    positions = new List<BlockPos>(set);
                    clientBlocksWaitingForPhoto.Remove(photoId);
                }
            }

            if (positions == null) return;

            mod.ClientApi.Event.EnqueueMainThreadTask(() =>
            {
                foreach (BlockPos p in positions)
                {
                    try
                    {
                        mod.ClientApi.World.BlockAccessor.MarkBlockDirty(p);
                    }
                    catch { }
                }
            }, "collodion-photo-arrived-markdirty");
        }

        public void ClientHandleAck(PhotoBlobAckPacket packet)
        {
            // Keep quiet unless failure.
            if (mod.ClientApi == null) return;
            if (packet == null) return;

            if (!packet.Ok)
            {
                mod.ClientApi.Logger.Warning($"Wetplate: photo transfer ack failed for {packet.PhotoId}: {packet.Error}");
            }
        }
    }
}
