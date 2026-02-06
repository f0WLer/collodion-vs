using Vintagestory.API.Client;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Collodion
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        private const float PhotoTargetAspect = 10f / 11f;
        private sealed class PlatePhotoTextureSource : ITexPositionSource
        {
            private readonly ITexPositionSource baseSource;
            private readonly TextureAtlasPosition photoTex;

            public PlatePhotoTextureSource(ITexPositionSource baseSource, TextureAtlasPosition photoTex)
            {
                this.baseSource = baseSource;
                this.photoTex = photoTex;
            }

            public TextureAtlasPosition this[string textureCode]
            {
                get
                {
                    if (string.Equals(textureCode, "plate", StringComparison.OrdinalIgnoreCase))
                    {
                        return photoTex;
                    }

                    return baseSource[textureCode];
                }
            }

            public Size2i AtlasSize => baseSource.AtlasSize;
        }

        private readonly object clientMeshLock = new object();
        private MeshData? clientPhotoMesh;
        private bool clientMeshQueued;
        private bool clientNeedsRebuild;
        private string? clientRenderSignature;

        partial void ClientPlateChanged(bool markBlockDirty)
        {
            if (Api?.Side != EnumAppSide.Client) return;

            clientNeedsRebuild = true;
            lock (clientMeshLock)
            {
                clientPhotoMesh = null;
            }
            clientRenderSignature = null;
            RequestClientMeshRebuild();

            if (!markBlockDirty) return;
            try
            {
                ((ICoreClientAPI)Api).World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch { }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Api?.Side != EnumAppSide.Client)
            {
                return base.OnTesselation(mesher, tessThreadTesselator);
            }

            // OnTesselation runs on the tesselation thread: only read cached meshes here.
            string? sig = null;
            try
            {
                lock (plateLock)
                {
                    sig = BlockEntityDevelopmentTray.ComputePlateSignature(PlateStack);
                }
            }
            catch
            {
                sig = null;
            }

            if (!string.Equals(clientRenderSignature, sig, StringComparison.Ordinal))
            {
                clientNeedsRebuild = true;
            }

            if (clientNeedsRebuild)
            {
                clientNeedsRebuild = false;
                RequestClientMeshRebuild();
            }

            MeshData? photoMesh;
            lock (clientMeshLock)
            {
                photoMesh = clientPhotoMesh;
            }

            if (photoMesh != null)
            {
                mesher.AddMeshData(photoMesh.Clone());
            }

            // Return false so the normal tray block mesh still renders.
            return base.OnTesselation(mesher, tessThreadTesselator);
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
                }, "collodion-devtray-rebuild");
            }
            catch
            {
                lock (clientMeshLock) clientMeshQueued = false;
            }
        }

        private void BuildClientMesh(ICoreClientAPI capi)
        {
            if (capi == null) return;

            ItemStack? plate;
            string? sig;
            lock (plateLock)
            {
                plate = PlateStack?.Clone();
                sig = BlockEntityDevelopmentTray.ComputePlateSignature(PlateStack);
            }

            if (plate?.Collectible?.Code == null)
            {
                lock (clientMeshLock) clientPhotoMesh = null;
                clientRenderSignature = sig;
                MarkDirty(true);
                return;
            }

            if (!ShouldShowTrayPhoto(plate))
            {
                lock (clientMeshLock) clientPhotoMesh = null;
                clientRenderSignature = sig;
                MarkDirty(true);
                return;
            }

            // Re-tesselate the tray shape but replace the "plate" texture with the photo.
            MeshData? photoMesh = null;
            if (PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, plate, out TextureAtlasPosition photoTex, out float photoAspect, Pos))
            {
                try
                {
                    ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                    ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, photoTex);

                    var shape = Block?.Shape?.Clone();
                    if (shape != null)
                    {
                        // Only keep the plate element so we don't duplicate the tray walls.
                        shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                        capi.Tesselator.TesselateShape(
                            "collodion-devtray-platephoto",
                            Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                            shape,
                            out photoMesh,
                            texSource
                        );

                        StampUvByRotationCropped(photoMesh, photoTex, 90, photoAspect, PhotoTargetAspect);

                        // Nudge up slightly to avoid z-fighting with the base plate.
                        photoMesh.Translate(0f, 0.0006f, 0f);
                    }
                }
                catch
                {
                    photoMesh = null;
                }
            }

            lock (clientMeshLock) clientPhotoMesh = photoMesh;
            clientRenderSignature = sig;

            MarkDirty(true);
            try
            {
                capi.World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch { }
        }


        private static bool ShouldShowTrayPhoto(ItemStack plate)
        {
            if (plate?.Attributes == null) return false;

            int pours = 0;
            try
            {
                pours = plate.Attributes.GetInt(WetPlateAttrs.DevelopPours, 0);
            }
            catch
            {
                pours = 0;
            }

            if (pours > 0) return true;

            string stage = plate.Attributes.GetString(WetPlateAttrs.PlateStage) ?? string.Empty;
            if (stage.Equals("developing", StringComparison.OrdinalIgnoreCase)) return true;
            if (stage.Equals("developed", StringComparison.OrdinalIgnoreCase)) return true;
            if (stage.Equals("finished", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static void StampUvByRotationCropped(MeshData mesh, TextureAtlasPosition texPos, int rotationDeg, float sourceAspect, float targetAspect)
        {
            if (mesh?.Uv == null || mesh.VerticesCount < 4) return;

            GetCroppedTexRect(texPos, sourceAspect, targetAspect, rotationDeg, out float x1, out float x2, out float y1, out float y2);

            int rot = ((rotationDeg % 360) + 360) % 360;
            int quadCount = mesh.VerticesCount / 4;
            for (int q = 0; q < quadCount; q++)
            {
                int v0 = (q * 4 + 0) * 2;
                int v1 = (q * 4 + 1) * 2;
                int v2 = (q * 4 + 2) * 2;
                int v3 = (q * 4 + 3) * 2;
                if (v3 + 1 >= mesh.Uv.Length) break;

                switch (rot)
                {
                    case 90:
                        mesh.Uv[v0] = x2; mesh.Uv[v0 + 1] = y2;
                        mesh.Uv[v1] = x2; mesh.Uv[v1 + 1] = y1;
                        mesh.Uv[v2] = x1; mesh.Uv[v2 + 1] = y1;
                        mesh.Uv[v3] = x1; mesh.Uv[v3 + 1] = y2;
                        break;
                    case 180:
                        mesh.Uv[v0] = x2; mesh.Uv[v0 + 1] = y1;
                        mesh.Uv[v1] = x1; mesh.Uv[v1 + 1] = y1;
                        mesh.Uv[v2] = x1; mesh.Uv[v2 + 1] = y2;
                        mesh.Uv[v3] = x2; mesh.Uv[v3 + 1] = y2;
                        break;
                    case 270:
                        mesh.Uv[v0] = x1; mesh.Uv[v0 + 1] = y1;
                        mesh.Uv[v1] = x1; mesh.Uv[v1 + 1] = y2;
                        mesh.Uv[v2] = x2; mesh.Uv[v2 + 1] = y2;
                        mesh.Uv[v3] = x2; mesh.Uv[v3 + 1] = y1;
                        break;
                    default:
                        mesh.Uv[v0] = x1; mesh.Uv[v0 + 1] = y2;
                        mesh.Uv[v1] = x2; mesh.Uv[v1 + 1] = y2;
                        mesh.Uv[v2] = x2; mesh.Uv[v2 + 1] = y1;
                        mesh.Uv[v3] = x1; mesh.Uv[v3 + 1] = y1;
                        break;
                }
            }

            for (int i = 0; i < mesh.Rgba.Length; i++) mesh.Rgba[i] = 255;
        }

        private static void GetCroppedTexRect(TextureAtlasPosition texPos, float sourceAspect, float targetAspect, int rotationDeg, out float x1, out float x2, out float y1, out float y2)
        {
            x1 = texPos.x1;
            x2 = texPos.x2;
            y1 = texPos.y1;
            y2 = texPos.y2;

            if (sourceAspect <= 0 || targetAspect <= 0) return;

            int rot = ((rotationDeg % 360) + 360) % 360;
            bool rot90 = rot == 90 || rot == 270;

            float effectiveSourceAspect = rot90 ? (1f / sourceAspect) : sourceAspect;
            if (effectiveSourceAspect <= 0) return;

            if (effectiveSourceAspect > targetAspect)
            {
                float keep = targetAspect / effectiveSourceAspect;
                if (keep < 0f) keep = 0f;
                if (keep > 1f) keep = 1f;
                float trim = (1f - keep) * 0.5f;

                if (!rot90)
                {
                    float xr = x2 - x1;
                    x1 += xr * trim;
                    x2 -= xr * trim;
                }
                else
                {
                    float yr = y2 - y1;
                    y1 += yr * trim;
                    y2 -= yr * trim;
                }

                return;
            }

            float keep2 = effectiveSourceAspect / targetAspect;
            if (keep2 < 0f) keep2 = 0f;
            if (keep2 > 1f) keep2 = 1f;
            float trim2 = (1f - keep2) * 0.5f;

            if (!rot90)
            {
                float yr = y2 - y1;
                y1 += yr * trim2;
                y2 -= yr * trim2;
            }
            else
            {
                float xr = x2 - x1;
                x1 += xr * trim2;
                x2 -= xr * trim2;
            }
        }
    }
}
