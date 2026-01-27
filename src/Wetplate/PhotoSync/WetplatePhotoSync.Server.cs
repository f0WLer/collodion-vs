using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Collodion
{
    public sealed partial class WetplatePhotoSync
    {
        // server-side: in-progress uploads by (playerUid|photoId)
        private readonly Dictionary<string, IncomingAssembly> serverIncoming = new Dictionary<string, IncomingAssembly>(StringComparer.OrdinalIgnoreCase);

        // Minimal cleanup so abandoned uploads (disconnects mid-transfer) don't accumulate.
        private long serverLastPruneMs;
        private const int ServerPruneIntervalMs = 30_000;
        private const int ServerUploadStaleMs = 120_000;

        private void ServerMaybePruneIncoming(long nowMs)
        {
            if (serverIncoming.Count == 0) return;
            if (serverLastPruneMs != 0 && (nowMs - serverLastPruneMs) < ServerPruneIntervalMs) return;
            serverLastPruneMs = nowMs;

            List<string>? toRemove = null;
            foreach (var kvp in serverIncoming)
            {
                var asm = kvp.Value;
                if (asm == null) continue;
                if ((nowMs - asm.LastTouchedMs) > ServerUploadStaleMs)
                {
                    toRemove ??= new List<string>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove == null) return;
            foreach (string key in toRemove)
            {
                serverIncoming.Remove(key);
            }
        }

        public void ServerHandleRequest(IServerPlayer fromPlayer, PhotoBlobRequestPacket packet)
        {
            if (mod.Api == null || mod.ServerChannel == null) return;
            if (fromPlayer == null || packet == null) return;

            string photoId = NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrEmpty(photoId)) return;

            string path = GetPhotoPath(photoId);
            if (!File.Exists(path))
            {
                mod.ServerChannel.SendPacket(new PhotoBlobAckPacket { PhotoId = photoId, Ok = false, Error = "Photo not present on server" }, fromPlayer);
                return;
            }

            mod.ServerTouchPhotoSeen(photoId);

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                mod.ServerChannel.SendPacket(new PhotoBlobAckPacket { PhotoId = photoId, Ok = false, Error = ex.Message }, fromPlayer);
                return;
            }

            if (bytes.Length <= 0 || bytes.Length > MaxBytes)
            {
                mod.ServerChannel.SendPacket(new PhotoBlobAckPacket { PhotoId = photoId, Ok = false, Error = "Photo too large" }, fromPlayer);
                return;
            }

            SendChunks(mod.ServerChannel, fromPlayer, photoId, bytes, isUpload: false);
        }

        public void ServerHandleChunk(IServerPlayer fromPlayer, PhotoBlobChunkPacket packet)
        {
            if (mod.Api == null || mod.ServerChannel == null) return;
            if (fromPlayer == null || packet == null) return;
            if (!packet.IsUpload) return; // ignore downloads on server

            long nowMs = Environment.TickCount64;
            ServerMaybePruneIncoming(nowMs);

            string photoId = NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrEmpty(photoId)) return;

            if (packet.TotalSize <= 0 || packet.TotalSize > MaxBytes) return;
            if (packet.ChunkCount <= 0 || packet.ChunkCount > 4096) return;
            if (packet.ChunkIndex < 0 || packet.ChunkIndex >= packet.ChunkCount) return;
            if (packet.Data == null) return;

            string key = fromPlayer.PlayerUID + "|" + photoId;

            if (!serverIncoming.TryGetValue(key, out IncomingAssembly? asm) || asm == null || asm.TotalSize != packet.TotalSize || asm.ChunkCount != packet.ChunkCount)
            {
                asm = new IncomingAssembly(packet.TotalSize, packet.ChunkCount);
                serverIncoming[key] = asm;
            }

            if (asm == null) return;

            asm.LastTouchedMs = nowMs;

            if (asm.Received[packet.ChunkIndex]) return;

            int offset = packet.ChunkIndex * ChunkSize;
            int copyLen = Math.Min(packet.Data.Length, asm.TotalSize - offset);
            if (copyLen <= 0) return;

            Buffer.BlockCopy(packet.Data, 0, asm.Buffer, offset, copyLen);
            asm.Received[packet.ChunkIndex] = true;
            asm.ReceivedChunks++;

            if (asm.ReceivedChunks < asm.ChunkCount) return;

            serverIncoming.Remove(key);

            // Basic PNG signature check
            if (asm.TotalSize < 8 || asm.Buffer[0] != 0x89 || asm.Buffer[1] != 0x50 || asm.Buffer[2] != 0x4E || asm.Buffer[3] != 0x47)
            {
                mod.ServerChannel.SendPacket(new PhotoBlobAckPacket { PhotoId = photoId, Ok = false, Error = "Invalid PNG" }, fromPlayer);
                return;
            }

            try
            {
                string outPath = GetPhotoPath(photoId);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, asm.Buffer);

                mod.ServerTouchPhotoSeen(photoId);

                mod.ServerChannel.SendPacket(new PhotoBlobAckPacket { PhotoId = photoId, Ok = true, Error = string.Empty }, fromPlayer);
            }
            catch (Exception ex)
            {
                mod.ServerChannel.SendPacket(new PhotoBlobAckPacket { PhotoId = photoId, Ok = false, Error = ex.Message }, fromPlayer);
            }
        }

    }
}
