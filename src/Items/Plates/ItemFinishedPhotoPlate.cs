using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Collodion
{
    public sealed class ItemFinishedPhotoPlate : ItemPlateBase
    {
        private const float GroundScale = 2.5f;
        private sealed class CachedRender
        {
            public MultiTextureMeshRef MeshRef;
            public int TextureId;

            public CachedRender(MultiTextureMeshRef meshRef, int textureId)
            {
                MeshRef = meshRef;
                TextureId = textureId;
            }
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CachedRender> MeshCache = new Dictionary<string, CachedRender>(StringComparer.OrdinalIgnoreCase);
        private static int AtlasVersion = 0;

        public static int ClearClientRenderCacheAndBumpVersion()
        {
            int cleared = 0;
            lock (CacheLock)
            {
                foreach (var kvp in MeshCache)
                {
                    kvp.Value.MeshRef.Dispose();
                    cleared++;
                }

                MeshCache.Clear();
                AtlasVersion++;
            }

            return cleared;
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            string photoId = itemstack?.Attributes?.GetString(WetPlateAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return;

            try
            {
                capi.ModLoader.GetModSystem<CollodionModSystem>()?.ClientMaybeSendPhotoSeen(photoId);
            }
            catch
            {
                // ignore
            }

            string photoFileName = WetplatePhotoSync.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoFileName)) return;

            int versionSnapshot;
            string cacheKey;
            lock (CacheLock)
            {
                versionSnapshot = AtlasVersion;

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

                int uvRotationDeg = 0;
                try
                {
                    // Optional tuning for when the plate model/UV orientation changes.
                    uvRotationDeg = itemstack?.Collectible?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
                }
                catch
                {
                    uvRotationDeg = 0;
                }

                // Default behavior: finished plates mirror the photo left-to-right.
                bool mirrorX = true;
                try
                {
                    mirrorX = itemstack?.Collectible?.Attributes?["photoMirrorX"]?.AsBool(true) ?? true;
                }
                catch
                {
                    mirrorX = true;
                }

                cacheKey = $"{photoFileName}|{variant}|r{((uvRotationDeg % 360) + 360) % 360}|mx{(mirrorX ? 1 : 0)}|v{versionSnapshot}";
                if (MeshCache.TryGetValue(cacheKey, out CachedRender? cached) && cached != null)
                {
                    renderinfo.ModelRef = cached.MeshRef;
                    renderinfo.TextureId = cached.TextureId;
                    return;
                }
            }

            string path = WetplatePhotoSync.GetPhotoPath(photoFileName);
            if (!File.Exists(path))
            {
                try
                {
                    CollodionModSystem.ClientInstance?.PhotoSync?.ClientRequestPhotoIfMissing(photoFileName);
                }
                catch { }
                return;
            }

            try
            {
                using (BitmapExternal bitmap = new BitmapExternal(path))
                {
                    string photoKey = Path.GetFileNameWithoutExtension(photoFileName);
                    AssetLocation texLoc = new AssetLocation("collodion", $"photo-{photoKey}-v{versionSnapshot}");

                    TextureAtlasPosition texPos;
                    int texSubId;

#pragma warning disable CS0618 // Using legacy atlas insert to preserve behavior on current VS version
                    capi.ItemTextureAtlas.InsertTextureCached(texLoc, (IBitmap)bitmap, out texSubId, out texPos, 0.05f);
#pragma warning restore CS0618

                    // Base mesh from the item shape/texture (plate-finished).
                    capi.Tesselator.TesselateItem(this, out MeshData baseMesh);

                    int uvRotationDeg = 0;
                    try
                    {
                        // Optional tuning for when the plate model/UV orientation changes.
                        uvRotationDeg = itemstack?.Collectible?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
                    }
                    catch
                    {
                        uvRotationDeg = 0;
                    }

                    bool mirrorX = true;
                    try
                    {
                        mirrorX = itemstack?.Collectible?.Attributes?["photoMirrorX"]?.AsBool(true) ?? true;
                    }
                    catch
                    {
                        mirrorX = true;
                    }

                    // Add a thin overlay quad on the "south" (+Z) face of the plate shape.
                    MeshData overlay = CreateFrontOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX);

                    baseMesh.AddMeshData(overlay);

                    if (target == EnumItemRenderTarget.Ground)
                    {
                        baseMesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), GroundScale, GroundScale, GroundScale);
                    }

                    int atlasTextureId = texPos.atlasTextureId;
                    MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(baseMesh);

                    lock (CacheLock)
                    {
                        if (AtlasVersion != versionSnapshot)
                        {
                            meshRef.Dispose();
                            return;
                        }

                        MeshCache[cacheKey] = new CachedRender(meshRef, atlasTextureId);
                    }

                    renderinfo.ModelRef = meshRef;
                    renderinfo.TextureId = atlasTextureId;
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render finished photo plate: " + ex);
            }
        }

        private static MeshData CreateFrontOverlayQuad(TextureAtlasPosition texPos, MeshData baseMesh, int uvRotationDeg, bool mirrorX)
        {
            // Derive the photo quad from the current plate model bounds so shape changes
            // (e.g. recent re-centering for GUI spin) don't break placement.
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            float[] xyz = baseMesh.xyz ?? System.Array.Empty<float>();
            int verts = baseMesh.VerticesCount;
            if (verts <= 0 || xyz.Length < verts * 3)
            {
                // Fallback: centered quad, should never happen for a real item mesh.
                minX = 0.1f;
                minY = 0.1f;
                maxX = 0.9f;
                maxY = 0.9f;
                maxZ = 0.5f;
                verts = 0;
            }

            for (int i = 0; i < verts; i++)
            {
                float x = xyz[i * 3 + 0];
                float y = xyz[i * 3 + 1];
                float z = xyz[i * 3 + 2];

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }

            // Push the overlay slightly forward to avoid z-fighting.
            const float eps = 0.0005f;
            float x1 = minX;
            float x2 = maxX;
            float y1 = minY;
            float y2 = maxY;
            float zFront = maxZ + eps;

            MeshData m = new MeshData(capacityVertices: 4, capacityIndices: 6, withNormals: false, withUv: true, withRgba: true, withFlags: true);

            m.SetXyz(new float[]
            {
                x1, y1, zFront,
                x2, y1, zFront,
                x2, y2, zFront,
                x1, y2, zFront
            });

            // UVs in 0..1 range (BL, BR, TR, TL).
            // Baseline UVs are un-mirrored; any rotation is applied via ApplyUvRotationCw().
            m.SetUv(new float[]
            {
                0f, 0f,
                1f, 0f,
                1f, 1f,
                0f, 1f
            });

            // Important: UV transforms iterate VerticesCount.
            m.SetVerticesCount(4);

            ApplyUvRotationCw(m, uvRotationDeg);

            if (mirrorX)
            {
                ApplyUvMirrorX(m);
            }

            m.Rgba.Fill((byte)255);

            // Required for texture routing.
            m.TextureIndicesCount = 1;
            m.XyzFaces = new byte[] { 3 };
            m.XyzFacesCount = 1;
            m.RenderPassesAndExtraBits = new short[] { 0 };
            m.RenderPassCount = 1;

            int packed = VertexFlags.PackNormal(0, 0, 1);
            for (int i = 0; i < 4; i++) m.Flags[i] = packed;

            m.SetIndices(new int[] { 0, 1, 2, 0, 2, 3 });
            m.SetIndicesCount(6);

            // Scale UVs into atlas space and fill TextureIndices.
            return m.WithTexPos(texPos);
        }

        private static void ApplyUvRotationCw(MeshData mesh, int uvRotationDeg)
        {
            int rot = ((uvRotationDeg % 360) + 360) % 360;
            if (rot == 0) return;

            float[] uv = mesh.Uv;
            for (int i = 0; i < mesh.VerticesCount; i++)
            {
                float u = uv[i * 2 + 0];
                float v = uv[i * 2 + 1];

                float u2;
                float v2;
                switch (rot)
                {
                    case 90:
                        u2 = 1f - v;
                        v2 = u;
                        break;
                    case 180:
                        u2 = 1f - u;
                        v2 = 1f - v;
                        break;
                    case 270:
                        u2 = v;
                        v2 = 1f - u;
                        break;
                    default:
                        return;
                }

                uv[i * 2 + 0] = u2;
                uv[i * 2 + 1] = v2;
            }
        }

        private static void ApplyUvMirrorX(MeshData mesh)
        {
            float[] uv = mesh.Uv;
            for (int i = 0; i < mesh.VerticesCount; i++)
            {
                uv[i * 2 + 0] = 1f - uv[i * 2 + 0];
            }
        }
    }
}
