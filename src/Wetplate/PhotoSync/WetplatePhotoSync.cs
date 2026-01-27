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
        private const int ChunkSize = 24 * 1024;
        private const int MaxBytes = 2 * 1024 * 1024; // plenty for 512x512 png

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

        private static void SendChunks(IClientNetworkChannel channel, string photoId, byte[] bytes, bool isUpload)
        {
            SendChunksCommon(photoId, bytes, isUpload, pkt => channel.SendPacket(pkt));
        }

        private static void SendChunks(IServerNetworkChannel channel, IServerPlayer toPlayer, string photoId, byte[] bytes, bool isUpload)
        {
            SendChunksCommon(photoId, bytes, isUpload, pkt => channel.SendPacket(pkt, toPlayer));
        }

        private static void SendChunksCommon(string photoId, byte[] bytes, bool isUpload, Action<PhotoBlobChunkPacket> send)
        {
            int chunkCount = (bytes.Length + ChunkSize - 1) / ChunkSize;
            for (int i = 0; i < chunkCount; i++)
            {
                int offset = i * ChunkSize;
                int len = Math.Min(ChunkSize, bytes.Length - offset);
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
