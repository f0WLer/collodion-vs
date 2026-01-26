using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Collodion
{
    public partial class ItemPhotograph
    {
        private const float GroundScale = 2.5f;

        // Maps the captured photo texture onto faces using texture keys "photo" or "null" in the item shape.
        private sealed class PhotoTextureSource : ITexPositionSource
        {
            private readonly ITexPositionSource baseSource;
            private readonly TextureAtlasPosition photoTex;

            public PhotoTextureSource(ITexPositionSource baseSource, TextureAtlasPosition photoTex)
            {
                this.baseSource = baseSource;
                this.photoTex = photoTex;
            }

            public TextureAtlasPosition this[string textureCode]
            {
                get
                {
                    if (string.Equals(textureCode, "null", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(textureCode, "photo", StringComparison.OrdinalIgnoreCase))
                    {
                        return photoTex;
                    }

                    return baseSource[textureCode];
                }
            }

            public Size2i AtlasSize => baseSource.AtlasSize;
        }

        private static readonly AssetLocation PhotoShapeBase = new AssetLocation("collodion", "item/photo");
        private class CachedPhotoRender
        {
            public MultiTextureMeshRef MeshRef;
            public int TextureId;

            public CachedPhotoRender(MultiTextureMeshRef meshRef, int textureId)
            {
                MeshRef = meshRef;
                TextureId = textureId;
            }
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CachedPhotoRender> PhotoMeshCache = new Dictionary<string, CachedPhotoRender>();

        // Bumping this version changes the atlas key used for inserted photo textures,
        // allowing on-disk image changes to be picked up without restarting.
        private static int AtlasVersion = 0;

        public static int ClearClientRenderCacheAndBumpVersion()
        {
            int cleared = 0;
            lock (CacheLock)
            {
                foreach (var kvp in PhotoMeshCache)
                {
                    kvp.Value.MeshRef.Dispose();
                    cleared++;
                }

                PhotoMeshCache.Clear();
                AtlasVersion++;
            }

            return cleared;
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            // Allow live pose tuning for photographs without affecting the camera.
            // Use `.collodion pose photo <fp|tp|gui|ground> ...` to tweak.
            try
            {
#pragma warning disable CS0618 // Keep existing FP pose handling
                var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();
                string poseKey = target switch
                {
                    EnumItemRenderTarget.HandFp => "photo-fp",
                    EnumItemRenderTarget.HandTp => "photo-tp",
                    EnumItemRenderTarget.Gui => "photo-gui",
                    EnumItemRenderTarget.Ground => "photo-ground",
                    _ => string.Empty
                };
#pragma warning restore CS0618

                if (!string.IsNullOrEmpty(poseKey))
                {
                    RenderPoseUtil.ApplyPoseDelta(modSys, poseKey, ref renderinfo);
                }
            }
            catch
            {
                // Ignore pose errors; rendering should still work.
            }

            string photoId = itemstack.Attributes.GetString("photoId") ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return;

            try
            {
                capi.ModLoader.GetModSystem<CollodionModSystem>()?.ClientMaybeSendPhotoSeen(photoId);
            }
            catch
            {
                // ignore
            }

            // Historically we store the filename (including extension) in attributes.
            // Normalize here so we don't accidentally look for "something.png.png".
            string photoFileName = WetplatePhotoSync.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoFileName)) return;

            int versionSnapshot;
            string cacheKey;
            lock (CacheLock)
            {
                versionSnapshot = AtlasVersion;

                // We need different meshes per render target because the "back" face needs a UV flip in-hand
                // (to look correct in third person), while GUI should remain unflipped.
                // NOTE: EnumItemRenderTarget.HandFp is obsolete in newer API, but still correct on 1.21.6.
#pragma warning disable CS0618
                string variant = target switch
                {
                    EnumItemRenderTarget.HandTp => "hand",
                    EnumItemRenderTarget.HandFp => "hand",
                    EnumItemRenderTarget.Ground => "ground",
                    _ => "gui"
                };
#pragma warning restore CS0618

                cacheKey = $"{photoFileName}|{variant}|v{versionSnapshot}";
                if (PhotoMeshCache.TryGetValue(cacheKey, out CachedPhotoRender? cached) && cached != null)
                {
                    renderinfo.ModelRef = cached.MeshRef;
                    renderinfo.TextureId = cached.TextureId;
                    return;
                }
            }
            {
                string path = WetplatePhotoSync.GetPhotoPath(photoFileName);
                if (File.Exists(path))
                {
                    try
                    {
                        using (BitmapExternal bitmap = new BitmapExternal(path))
                        {
                            string photoKey = Path.GetFileNameWithoutExtension(photoFileName);
                            AssetLocation texLoc = new AssetLocation("collodion", $"photo-{photoKey}-v{versionSnapshot}");

                            TextureAtlasPosition texPos;
                            int texSubId;

                            // NOTE: This is marked obsolete in the API, but still works on 1.21.6.
#pragma warning disable CS0618 // Using legacy atlas insert to preserve behavior on current VS version
                            capi.ItemTextureAtlas.InsertTextureCached(texLoc, (IBitmap)bitmap, out texSubId, out texPos, 0.05f);
#pragma warning restore CS0618

                            // Build the mesh from the item shape, mapping the photo texture onto the "Photo" element.
                            MeshData modelData = BuildPhotoMeshFromItemShape(capi, texPos);

                            // Apply the "back face" UV flip only for hand rendering.
                            // If we flip unconditionally, whichever target builds the cached mesh first will
                            // affect all other targets (e.g., TP correct but GUI mirrored).
                            // NOTE: EnumItemRenderTarget.HandFp is obsolete in newer API, but still correct on 1.21.6.
#pragma warning disable CS0618
                            if (target == EnumItemRenderTarget.HandTp || target == EnumItemRenderTarget.HandFp)
#pragma warning restore CS0618
                            {
                                FlipUvForMaxZFace(modelData, texPos);
                            }

                            if (target == EnumItemRenderTarget.Ground)
                            {
                                modelData.Scale(new Vec3f(0.5f, 0.5f, 0.5f), GroundScale, GroundScale, GroundScale);
                            }

                            int atlasTextureId = texPos.atlasTextureId;

                            MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(modelData);

                            lock (CacheLock)
                            {
                                // If cache was cleared/bumped while we were building, don't keep stale versions around.
                                if (AtlasVersion != versionSnapshot)
                                {
                                    meshRef.Dispose();
                                    return;
                                }

                                PhotoMeshCache[cacheKey] = new CachedPhotoRender(meshRef, atlasTextureId);
                            }
                            renderinfo.ModelRef = meshRef;
                            renderinfo.TextureId = atlasTextureId;
                        }
                    }
                    catch (Exception ex)
                    {
                        capi.Logger.Error("Failed to load wetplate photo: " + ex.ToString());
                    }
                }
                else
                {
                    try
                    {
                        CollodionModSystem.ClientInstance?.PhotoSync?.ClientRequestPhotoIfMissing(photoFileName);
                    }
                    catch { }
                }
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            if (api.Side == EnumAppSide.Client)
            {
                lock (CacheLock)
                {
                    foreach (var kvp in PhotoMeshCache)
                    {
                        kvp.Value.MeshRef.Dispose();
                    }
                    PhotoMeshCache.Clear();
                }
            }
        }

        private MeshData BuildPhotoMeshFromItemShape(ICoreClientAPI capi, TextureAtlasPosition texPos)
        {
            try
            {
                ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(this);
                var photoSource = new PhotoTextureSource(baseSource, texPos);

                // The shape should use texture key "#photo" (preferred) or "#null" on the photo face.
                var composite = new CompositeShape { Base = PhotoShapeBase };

                capi.Tesselator.TesselateShape(
                    "collodion-photoitem",
                    new AssetLocation("collodion", "photoitem"),
                    composite,
                    out MeshData mesh,
                    photoSource
                );

                mesh.Rgba?.Fill((byte)255);
                return mesh;
            }
            catch
            {
                // Fall back to a simple thin "photo card" mesh.
            }

            // Legacy fallback: simple thin "photo card" mesh.
            // IMPORTANT: keep the plate thin in Z (not X), otherwise the item will
            // look like it's laying on its side for most render targets.
            MeshData fallback = CubeMeshUtil.GetCube();
            fallback.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 0.5f, 0.5f, 0.06f);
            fallback.Rgba.Fill((byte)255);
            return fallback;
        }

        private static void FlipUvForMaxZFace(MeshData mesh, TextureAtlasPosition texPos)
        {
            if (mesh.xyz == null || mesh.Uv == null) return;

            int verts = mesh.VerticesCount;
            if (verts <= 0 || mesh.xyz.Length < verts * 3 || mesh.Uv.Length < verts * 2) return;

            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < verts; i++)
            {
                float z = mesh.xyz[i * 3 + 2];
                if (z > maxZ) maxZ = z;
            }

            float eps = 0.0001f;
            float sumU = texPos.x1 + texPos.x2;
            for (int i = 0; i < verts; i++)
            {
                float z = mesh.xyz[i * 3 + 2];
                if (Math.Abs(z - maxZ) > eps) continue;
                int uvIndex = i * 2;
                mesh.Uv[uvIndex] = sumU - mesh.Uv[uvIndex];
            }
        }
    }
}
