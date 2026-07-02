using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Photocore.Configuration;
using Photocore.PhotoSync;

namespace Photocore.PhotoSync.Runtime
{
    public sealed partial class PhotoAssetSyncCore
    {
        private readonly PhotocoreModSystem _mod;

        private sealed class IncomingAssembly
        {
            public readonly int TotalSize;
            public readonly int ChunkCount;

            public readonly byte[] Buffer;

            public int ReceivedChunks;
            public readonly bool[] Received;

            public long LastTouchedMs;

            public IncomingAssembly(int totalSize, int chunkCount)
            {
                TotalSize = totalSize;
                ChunkCount = chunkCount;
                Buffer = new byte[totalSize];
                Received = new bool[chunkCount];
                ReceivedChunks = 0;
                LastTouchedMs = Environment.TickCount64;
            }
        }

        public PhotoAssetSyncCore(PhotocoreModSystem mod)
        {
            _mod = mod;
        }

        private PhotoSyncConfig? SyncCfg => _mod?.Config?.PhotoSync;

        private void SendChunksConfigured(IClientNetworkChannel channel, string photoId, byte[] bytes, bool isUpload)
        {
            SendChunksCommon(photoId, bytes, isUpload, GetChunkSizeBytes(), pkt => channel.SendPacket(pkt));
        }

        private void SendChunksConfigured(IServerNetworkChannel channel, IServerPlayer toPlayer, string photoId, byte[] bytes, bool isUpload)
        {
            SendChunksCommon(photoId, bytes, isUpload, GetChunkSizeBytes(), pkt => channel.SendPacket(pkt, toPlayer));
        }

        private static void SendChunksCommon(string photoId, byte[] bytes, bool isUpload, int chunkSizeBytes, Action<PhotoBlobChunkPacket> send)
        {
            int chunkSize = Math.Max(1024, chunkSizeBytes);
            int chunkCount = (bytes.Length + chunkSize - 1) / chunkSize;
            for (int i = 0; i < chunkCount; i++)
            {
                int offset = i * chunkSize;
                int len = Math.Min(chunkSize, bytes.Length - offset);
                byte[] chunk = new byte[len];
                Buffer.BlockCopy(bytes, offset, chunk, 0, len);

                send(new PhotoBlobChunkPacket
                {
                    PhotoId = photoId,
                    TotalSize = bytes.Length,
                    ChunkIndex = i,
                    ChunkCount = chunkCount,
                    Data = chunk,
                    IsUpload = isUpload,
                    ChunkOffset = offset
                });
            }
        }

        private static bool IsChunkPacketShapeValid(PhotoBlobChunkPacket packet, int maxTransferBytes)
        {
            if (packet == null) return false;
            if (packet.TotalSize <= 0 || packet.TotalSize > maxTransferBytes) return false;
            if (packet.ChunkCount <= 0 || packet.ChunkCount > 4096) return false;
            if (packet.ChunkIndex < 0 || packet.ChunkIndex >= packet.ChunkCount) return false;
            if (packet.Data == null || packet.Data.Length <= 0) return false;
            if (packet.ChunkOffset < 0 || packet.ChunkOffset >= packet.TotalSize) return false;

            // Enforce strict offset/index shape so old packets without ChunkOffset are rejected.
            if (packet.ChunkIndex == 0)
            {
                if (packet.ChunkOffset != 0) return false;
            }
            else if (packet.ChunkOffset < packet.ChunkIndex)
            {
                return false;
            }

            if (packet.Data.Length > packet.TotalSize - packet.ChunkOffset) return false;
            return true;
        }

        private static bool TryNormalizePhotoId(string rawPhotoId, out string photoId)
        {
            photoId = PhotoAssetStoragePaths.NormalizePhotoId(rawPhotoId);
            return !string.IsNullOrEmpty(photoId);
        }

        private bool TryNormalizeAndValidateChunkPacket(PhotoBlobChunkPacket packet, out string photoId)
        {
            if (!TryNormalizePhotoId(packet.PhotoId, out photoId)) return false;

            return IsChunkPacketShapeValid(packet, GetMaxTransferBytes());
        }

        private static bool TryApplyChunkToAssembly(IncomingAssembly asm, PhotoBlobChunkPacket packet)
        {
            if (asm.Received[packet.ChunkIndex]) return false;
            if (packet.ChunkOffset + packet.Data.Length > asm.TotalSize) return false;

            Buffer.BlockCopy(packet.Data, 0, asm.Buffer, packet.ChunkOffset, packet.Data.Length);
            asm.Received[packet.ChunkIndex] = true;
            asm.ReceivedChunks++;
            return true;
        }

        private static bool TryProcessIncomingChunk(
            Dictionary<string, IncomingAssembly> incomingByKey,
            object incomingLock,
            string assemblyKey,
            PhotoBlobChunkPacket packet,
            long nowMs,
            out IncomingAssembly? completed)
        {
            completed = null;

            lock (incomingLock)
            {
                if (!incomingByKey.TryGetValue(assemblyKey, out IncomingAssembly? asm)
                    || asm == null
                    || asm.TotalSize != packet.TotalSize
                    || asm.ChunkCount != packet.ChunkCount)
                {
                    asm = new IncomingAssembly(packet.TotalSize, packet.ChunkCount);
                    incomingByKey[assemblyKey] = asm;
                }

                asm.LastTouchedMs = nowMs;

                if (!TryApplyChunkToAssembly(asm, packet)) return false;
                if (asm.ReceivedChunks < asm.ChunkCount) return false;

                incomingByKey.Remove(assemblyKey);
                completed = asm;
                return true;
            }
        }

        private static void PruneStaleIncomingAssemblies(
            Dictionary<string, IncomingAssembly> incomingByKey,
            object incomingLock,
            long nowMs,
            long staleAfterMs)
        {
            lock (incomingLock)
            {
                if (incomingByKey.Count == 0) return;

                List<string>? staleKeys = null;
                foreach (KeyValuePair<string, IncomingAssembly> kvp in incomingByKey)
                {
                    IncomingAssembly asm = kvp.Value;
                    if (asm == null) continue;
                    if (nowMs - asm.LastTouchedMs <= staleAfterMs) continue;

                    staleKeys ??= new List<string>();
                    staleKeys.Add(kvp.Key);
                }

                if (staleKeys == null) return;
                foreach (string key in staleKeys)
                {
                    incomingByKey.Remove(key);
                }
            }
        }

        private static bool TryWritePhotoBytes(string photoId, byte[] bytes, object? writeLock, out string? error)
        {
            error = null;

            try
            {
                string outPath = PhotoAssetStoragePaths.GetPhotoPath(photoId);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                if (writeLock == null)
                {
                    File.WriteAllBytes(outPath, bytes);
                }
                else
                {
                    lock (writeLock)
                    {
                        File.WriteAllBytes(outPath, bytes);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool LooksLikePng(byte[] buffer, int totalSize)
        {
            return totalSize >= 8
                && buffer.Length >= 8
                && buffer[0] == 0x89
                && buffer[1] == 0x50
                && buffer[2] == 0x4E
                && buffer[3] == 0x47;
        }
    }
}
