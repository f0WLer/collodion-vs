using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Collodion
{
    public partial class BlockEntityPhotograph
    {
        private readonly object clientMeshLock = new object();
        private MeshData? clientMesh;
        private MeshData? clientFrameMesh;
        private TextureAtlasPosition? clientTexPos;
        private int clientTextureId;

        private bool clientMeshQueued;
        private bool clientNeedsRebuild;
        private string? clientLastError;
        private string? clientPhotoPath;
        private bool clientPhotoFileExists;
        private string? clientOverlayInfo;

        private int clientMeshBuildCount;
        private int clientTesselationCount;

        private void RequestClientMeshRebuild()
        {
            if (Api?.Side != EnumAppSide.Client) return;

            ICoreClientAPI capi = (ICoreClientAPI)Api;
            lock (clientMeshLock)
            {
                if (clientMeshQueued) return;
                clientMeshQueued = true;
            }

            try
            {
                capi.Event.EnqueueMainThreadTask(() =>
                {
                    lock (clientMeshLock) clientMeshQueued = false;
                    BuildClientMesh(capi);
                }, "collodion-photograph-rebuild");
            }
            catch
            {
                lock (clientMeshLock) clientMeshQueued = false;
            }
        }

        private void BuildClientMesh(ICoreClientAPI capi)
        {
            if (capi == null) return;

            // Only generate a frame override mesh when a plank has been applied.
            // When null, the default oak JSON mesh will render as normal.
            MeshData? frameMesh = string.IsNullOrWhiteSpace(FramePlankBlockCode) ? null : GenerateFrameMeshForBlock(capi);

            if (string.IsNullOrWhiteSpace(PhotoId))
            {
                lock (clientMeshLock)
                {
                    clientMesh = null;
                    clientFrameMesh = frameMesh;
                    clientTexPos = null;
                    clientTextureId = 0;
                    clientLastError = null;
                    clientPhotoPath = null;
                    clientPhotoFileExists = false;
                    clientOverlayInfo = null;
                }
                MarkDirty(true);
                return;
            }

            try
            {
                string photoId = PhotoId!;
                string photoFileName = WetplatePhotoSync.NormalizePhotoId(photoId);
                string photoPath = WetplatePhotoSync.GetPhotoPath(photoFileName);

                lock (clientMeshLock)
                {
                    clientPhotoPath = photoPath;
                    clientPhotoFileExists = File.Exists(photoPath);
                }

                if (!File.Exists(photoPath))
                {
                    lock (clientMeshLock)
                    {
                        clientMesh = null;
                        clientFrameMesh = frameMesh;
                        clientTexPos = null;
                        clientTextureId = 0;
                        clientLastError = $"Photo file not found: {photoFileName}";
                        clientOverlayInfo = null;
                    }

                    // If the photo isn't present locally, request it from the server.
                    // Also note that this block is waiting, so when the photo arrives we can re-tesselate.
                    try
                    {
                        var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();
                        modSys?.PhotoSync?.ClientNoteBlockWaitingForPhoto(photoFileName, Pos);
                        modSys?.PhotoSync?.ClientRequestPhotoIfMissing(photoFileName);
                    }
                    catch
                    {
                        // ignore
                    }

                    MarkDirty(true);
                    return;
                }

                byte[] pngBytes = File.ReadAllBytes(photoPath);

                string textureKey = $"photo-{Path.GetFileNameWithoutExtension(photoFileName)}";
                AssetLocation texLoc = new AssetLocation("collodion", textureKey);

                TextureAtlasPosition texPos;
                capi.BlockTextureAtlas.GetOrInsertTexture(
                    texLoc,
                    out clientTextureId,
                    out texPos,
                    () => capi.Render.BitmapCreateFromPng(pngBytes),
                    0.05f
                );

                MeshData newMesh = GeneratePhotoMeshForBlock(capi, texPos);
                lock (clientMeshLock)
                {
                    clientTexPos = texPos;
                    clientMesh = newMesh;
                    clientFrameMesh = frameMesh;
                    clientLastError = null;
                }

                clientMeshBuildCount++;
                MarkDirty(true);

                try
                {
                    capi.World.BlockAccessor.MarkBlockDirty(Pos);
                }
                catch { }
            }
            catch (Exception ex)
            {
                lock (clientMeshLock)
                {
                    clientMesh = null;
                    clientFrameMesh = frameMesh;
                    clientTexPos = null;
                    clientTextureId = 0;
                    clientLastError = $"Exception building mesh: {ex.GetType().Name}: {ex.Message}";
                }
                MarkDirty(true);
            }
        }
    }
}
