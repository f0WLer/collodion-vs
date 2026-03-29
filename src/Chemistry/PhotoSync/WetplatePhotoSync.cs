using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Collodion
{
    public sealed partial class WetplatePhotoSync
    {
        private const int DefaultChunkSize = 24 * 1024;
        private const int DefaultMaxBytes = 2 * 1024 * 1024; // plenty for configured capture sizes

        private readonly CollodionModSystem mod;

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

        public WetplatePhotoSync(CollodionModSystem mod)
        {
            this.mod = mod;
        }

        private PhotoSyncConfig? SyncCfg => mod?.Config?.PhotoSync;

        private int GetChunkSizeBytes()
        {
            int size = SyncCfg?.ChunkSizeBytes ?? DefaultChunkSize;
            if (size < 1024) size = 1024;
            return size;
        }

        private int GetMaxTransferBytes()
        {
            int max = SyncCfg?.MaxTransferBytes ?? DefaultMaxBytes;
            if (max < 16 * 1024) max = 16 * 1024;
            return max;
        }

        public static string NormalizePhotoId(string photoId)
        {
            if (string.IsNullOrWhiteSpace(photoId)) return string.Empty;
            photoId = photoId.Trim();
            if (!photoId.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) photoId += ".png";
            return photoId;
        }

        public static string GetPhotoPath(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            return Path.Combine(GamePaths.DataPath, "ModData", "collodion", "photos", normalized);
        }

        // -------------------- Chunk helpers --------------------

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
                    IsUpload = isUpload
                });
            }
        }
    }
}
