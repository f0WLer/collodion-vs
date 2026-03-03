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
            bool hasFrameOverride = !string.IsNullOrWhiteSpace(FramePlankBlockCode) || !string.IsNullOrWhiteSpace(FramePlankBlockCode2);
            MeshData? frameMesh = hasFrameOverride ? GenerateFrameMeshForBlock(capi) : null;

            string? primaryPhotoId = string.IsNullOrWhiteSpace(PhotoId) ? null : PhotoId;
            string? secondaryPhotoId = string.IsNullOrWhiteSpace(PhotoId2) ? null : PhotoId2;

            if (primaryPhotoId == null && secondaryPhotoId == null)
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
                MeshData? newMesh = null;
                TextureAtlasPosition? firstTexPos = null;
                string? firstPhotoPath = null;
                bool firstPhotoExists = false;
                string? firstError = null;

                if (!string.IsNullOrWhiteSpace(primaryPhotoId))
                {
                    if (TryBuildPhotoMeshForId(capi, primaryPhotoId!, 1, out MeshData? photoMesh1, out TextureAtlasPosition? texPos1, out string? path1, out bool exists1, out string? error1))
                    {
                        newMesh = photoMesh1;
                        firstTexPos = texPos1;
                        firstPhotoPath = path1;
                        firstPhotoExists = exists1;
                    }
                    else
                    {
                        firstError = error1;
                    }
                }

                if (!string.IsNullOrWhiteSpace(secondaryPhotoId))
                {
                    if (TryBuildPhotoMeshForId(capi, secondaryPhotoId!, 2, out MeshData? photoMesh2, out TextureAtlasPosition? texPos2, out string? path2, out bool exists2, out string? error2))
                    {
                        if (newMesh == null)
                        {
                            newMesh = photoMesh2;
                            firstTexPos = texPos2;
                            firstPhotoPath = path2;
                            firstPhotoExists = exists2;
                        }
                        else
                        {
                            newMesh.AddMeshData(photoMesh2);
                        }
                    }
                    else if (firstError == null)
                    {
                        firstError = error2;
                    }
                }

                if (newMesh == null)
                {
                    lock (clientMeshLock)
                    {
                        clientMesh = null;
                        clientFrameMesh = frameMesh;
                        clientTexPos = null;
                        clientTextureId = 0;
                        clientLastError = firstError ?? "Failed to prepare framed photo texture";
                        clientOverlayInfo = null;
                    }

                    MarkDirty(true);
                    return;
                }

                clientTextureId = firstTexPos?.atlasTextureId ?? 0;
                lock (clientMeshLock)
                {
                    clientPhotoPath = firstPhotoPath;
                    clientPhotoFileExists = firstPhotoExists;
                    clientTexPos = firstTexPos;
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

        private bool TryBuildPhotoMeshForId(ICoreClientAPI capi, string photoId, int photoSlot, out MeshData? mesh, out TextureAtlasPosition? texPos, out string photoPath, out bool photoExists, out string? error)
        {
            mesh = null;
            texPos = null;
            error = null;

            string photoFileName = WetplatePhotoSync.NormalizePhotoId(photoId);
            photoPath = WetplatePhotoSync.GetPhotoPath(photoFileName);
            photoExists = File.Exists(photoPath);

            if (!photoExists)
            {
                error = $"Photo file not found: {photoFileName}";

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

                return false;
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

            if (frameStack == null || !PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, frameStack, out TextureAtlasPosition localTexPos, out float photoAspect, Pos))
            {
                error = $"Failed to prepare frame photo texture: {photoFileName}";
                return false;
            }

            mesh = GeneratePhotoMeshForBlock(capi, localTexPos, photoAspect, photoSlot);
            texPos = localTexPos;
            return true;
        }
    }
}
