using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

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

        private static bool TryGetPngDimensions(byte[] pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            // PNG header (8 bytes) + IHDR chunk length/type (8 bytes) + IHDR data begins.
            // Width/Height are 4 bytes each, big-endian, at offsets 16 and 20.
            if (pngBytes == null || pngBytes.Length < 24) return false;

            // Validate PNG signature: 89 50 4E 47 0D 0A 1A 0A
            if (pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47
                || pngBytes[4] != 0x0D || pngBytes[5] != 0x0A || pngBytes[6] != 0x1A || pngBytes[7] != 0x0A)
            {
                return false;
            }

            try
            {
                width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
                height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
            }
            catch
            {
                width = 0;
                height = 0;
                return false;
            }

            return width > 0 && height > 0;
        }

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
                bool photoExists = File.Exists(photoPath);

                lock (clientMeshLock)
                {
                    clientPhotoPath = photoPath;
                    clientPhotoFileExists = photoExists;
                }

                if (!photoExists)
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

                ItemStack? frameStack = null;
                try
                {
                    Item? frameItem = capi.World.GetItem(new AssetLocation("collodion", "framedphotograph"));
                    if (frameItem != null)
                    {
                        frameStack = new ItemStack(frameItem);
                        var attrs = new TreeAttribute();
                        attrs.SetString(WetPlateAttrs.PhotoId, photoId);
                        if (ExposureMovement > 0f)
                        {
                            attrs.SetFloat(WetPlateAttrs.HoldStillMovement, ExposureMovement);
                        }
                        frameStack.Attributes = attrs;
                    }
                }
                catch
                {
                    frameStack = null;
                }

                if (frameStack == null || !PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, frameStack, out TextureAtlasPosition texPos, out float photoAspect, Pos))
                {
                    lock (clientMeshLock)
                    {
                        clientMesh = null;
                        clientFrameMesh = frameMesh;
                        clientTexPos = null;
                        clientTextureId = 0;
                        clientLastError = $"Failed to prepare frame photo texture: {photoFileName}";
                        clientOverlayInfo = null;
                    }

                    MarkDirty(true);
                    return;
                }

                clientTextureId = texPos.atlasTextureId;

                MeshData newMesh = GeneratePhotoMeshForBlock(capi, texPos, photoAspect);
                lock (clientMeshLock)
                {
                    clientTexPos = texPos;
                    clientMesh = newMesh;
                    clientFrameMesh = frameMesh;
                    clientLastError = null;
                }

                clientMeshBuildCount++;
                MarkDirty(true);
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
