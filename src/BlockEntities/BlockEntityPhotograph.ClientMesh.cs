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
                bool builtMesh = TryBuildCombinedPhotoMesh(
                    capi, primaryPhotoId, secondaryPhotoId,
                    out MeshData? newMesh,
                    out TextureAtlasPosition? firstTexPos,
                    out string? firstPhotoPath,
                    out bool firstPhotoExists,
                    out string? firstError);

                if (!builtMesh || newMesh == null)
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

        // Iterates primary then secondary photo slots, tessellates each, and merges
        // them into a single combined mesh. Returns false if no mesh could be built.
        private bool TryBuildCombinedPhotoMesh(
            ICoreClientAPI capi,
            string? primaryPhotoId,
            string? secondaryPhotoId,
            out MeshData? mesh,
            out TextureAtlasPosition? texPos,
            out string? photoPath,
            out bool photoExists,
            out string? error)
        {
            mesh = null;
            texPos = null;
            photoPath = null;
            photoExists = false;
            error = null;

            if (!string.IsNullOrWhiteSpace(primaryPhotoId))
            {
                if (TryBuildPhotoMeshForId(capi, primaryPhotoId!, 1, out MeshData? photoMesh1, out TextureAtlasPosition? texPos1, out string? path1, out bool exists1, out string? error1))
                {
                    mesh = photoMesh1;
                    texPos = texPos1;
                    photoPath = path1;
                    photoExists = exists1;
                }
                else
                {
                    error = error1;
                }
            }

            if (!string.IsNullOrWhiteSpace(secondaryPhotoId))
            {
                if (TryBuildPhotoMeshForId(capi, secondaryPhotoId!, 2, out MeshData? photoMesh2, out TextureAtlasPosition? texPos2, out string? path2, out bool exists2, out string? error2))
                {
                    if (mesh == null)
                    {
                        mesh = photoMesh2;
                        texPos = texPos2;
                        photoPath = path2;
                        photoExists = exists2;
                    }
                    else
                    {
                        mesh.AddMeshData(photoMesh2);
                    }
                }
                else if (error == null)
                {
                    error = error2;
                }
            }

            return mesh != null;
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
                    var modSys = CollodionConfigAccess.ResolveClientModSystem(capi);
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
                        attrs.SetDouble(WetPlateAttrs.HoldStillMovement, ExposureMovement);
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
