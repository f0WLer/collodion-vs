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
    public static class PhotoPlateRenderUtil
    {
        private const float GroundScale = 2.5f;
        // Plate/frame visible area is 5w x 5.5h => aspect = 10/11.
        private const float PhotoTargetAspect = 10f / 11f;
        private const int DevelopPoursRequired = 5;

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

            // Clear derived photo cache (best effort)
            try
            {
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "collodion", "photos", "derived");
                if (Directory.Exists(derivedDir))
                {
                    Directory.Delete(derivedDir, true);
                }
            }
            catch
            {
                // ignore
            }

            return cleared;
        }

        public static bool ShouldRenderPhotoOverlay(ItemStack? itemstack)
        {
            if (itemstack?.Collectible == null) return false;
            try
            {
                return itemstack.Collectible.Attributes?["renderPhotoOverlay"]?.AsBool(false) ?? false;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRenderPhotoOverlay(ICoreClientAPI capi, ItemStack? itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (capi == null || itemstack == null) return false;

            string photoId = itemstack.Attributes?.GetString(WetPlateAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return false;

            string effectsProfile = string.Empty;
            try
            {
                effectsProfile = itemstack.Collectible?.Attributes?["photoEffectsProfile"]?.AsString(string.Empty) ?? string.Empty;
            }
            catch
            {
                effectsProfile = string.Empty;
            }

            try
            {
                capi.ModLoader.GetModSystem<CollodionModSystem>()?.ClientMaybeSendPhotoSeen(photoId);
            }
            catch
            {
                // ignore
            }

            string photoFileName = WetplatePhotoSync.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoFileName)) return false;

            int uvRotationDeg;
            try
            {
                // Optional tuning for when the plate model/UV orientation changes.
                uvRotationDeg = itemstack.Collectible?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
            }
            catch
            {
                uvRotationDeg = 0;
            }

            bool mirrorX;
            try
            {
                // Default behavior: finished plates mirror the photo left-to-right.
                mirrorX = itemstack.Collectible?.Attributes?["photoMirrorX"]?.AsBool(true) ?? true;
            }
            catch
            {
                mirrorX = true;
            }

            int developPours = DevelopPoursRequired;
            if (!string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    developPours = itemstack.Attributes?.GetInt(WetPlateAttrs.DevelopPours, DevelopPoursRequired) ?? DevelopPoursRequired;
                }
                catch
                {
                    developPours = DevelopPoursRequired;
                }

                if (developPours < 0) developPours = 0;
                if (developPours > DevelopPoursRequired) developPours = DevelopPoursRequired;
            }

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

                cacheKey = $"{photoFileName}|{variant}|r{((uvRotationDeg % 360) + 360) % 360}|mx{(mirrorX ? 1 : 0)}|fx{effectsProfile}|dp{developPours}|v{versionSnapshot}";
                if (MeshCache.TryGetValue(cacheKey, out CachedRender? cached) && cached != null)
                {
                    renderinfo.ModelRef = cached.MeshRef;
                    renderinfo.TextureId = cached.TextureId;
                    return true;
                }
            }

            string sourcePath = WetplatePhotoSync.GetPhotoPath(photoFileName);
            if (!File.Exists(sourcePath))
            {
                try
                {
                    CollodionModSystem.ClientInstance?.PhotoSync?.ClientRequestPhotoIfMissing(photoFileName);
                }
                catch { }
                return false;
            }

            string renderPath = sourcePath;
            string renderFileName = photoFileName;
            if (!string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase))
            {
                string derivedFileName = GetDerivedPhotoFileName(photoFileName, $"developed{developPours}");
                string derivedPath = GetDerivedPhotoPath(photoFileName, $"developed{developPours}");

                if (TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|developed|{developPours}", developPours))
                {
                    renderPath = derivedPath;
                    renderFileName = derivedFileName;
                }
            }

            try
            {
                using (BitmapExternal bitmap = new BitmapExternal(renderPath))
                {
                    float photoAspect = 1f;
                    try
                    {
                        if (bitmap.Height > 0) photoAspect = bitmap.Width / (float)bitmap.Height;
                    }
                    catch
                    {
                        photoAspect = 1f;
                    }

                    string photoKey = Path.GetFileNameWithoutExtension(renderFileName);
                    AssetLocation texLoc = new AssetLocation("collodion", $"photo-{photoKey}-v{versionSnapshot}");

                    TextureAtlasPosition texPos;
                    int texSubId;

#pragma warning disable CS0618 // Using legacy atlas insert to preserve behavior on current VS version
                    capi.ItemTextureAtlas.InsertTextureCached(texLoc, (IBitmap)bitmap, out texSubId, out texPos, 0.05f);
#pragma warning restore CS0618

                    Item? item = itemstack.Collectible as Item;
                    if (item == null) return false;

                    // Base mesh from the item shape/texture (plate-finished / plate-developed).
                    capi.Tesselator.TesselateItem(item, out MeshData baseMesh);

                    // Add a thin overlay quad on the "south" (+Z) face of the plate shape.
                    MeshData overlay = CreateFrontOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect);

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
                            return false;
                        }

                        MeshCache[cacheKey] = new CachedRender(meshRef, atlasTextureId);
                    }

                    renderinfo.ModelRef = meshRef;
                    renderinfo.TextureId = atlasTextureId;
                    return true;
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render photo plate overlay: " + ex);
            }

            return false;
        }

        public static bool TryGetPhotoBlockTexture(ICoreClientAPI capi, ItemStack? itemstack, out TextureAtlasPosition texPos, out float photoAspect, BlockPos? waitingPos = null)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
            photoAspect = 1f;

            if (capi == null || itemstack == null) return false;

            string photoId = itemstack.Attributes?.GetString(WetPlateAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return false;

            try
            {
                capi.ModLoader.GetModSystem<CollodionModSystem>()?.ClientMaybeSendPhotoSeen(photoId);
            }
            catch
            {
                // ignore
            }

            string effectsProfile = string.Empty;
            try
            {
                effectsProfile = itemstack.Collectible?.Attributes?[
                    "photoEffectsProfile"]?.AsString(string.Empty) ?? string.Empty;
            }
            catch
            {
                effectsProfile = string.Empty;
            }

            string photoFileName = WetplatePhotoSync.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoFileName)) return false;

            int developPours = DevelopPoursRequired;
            if (!string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    developPours = itemstack.Attributes?.GetInt(WetPlateAttrs.DevelopPours, DevelopPoursRequired) ?? DevelopPoursRequired;
                }
                catch
                {
                    developPours = DevelopPoursRequired;
                }

                if (developPours < 0) developPours = 0;
                if (developPours > DevelopPoursRequired) developPours = DevelopPoursRequired;
            }

            int versionSnapshot;
            lock (CacheLock) versionSnapshot = AtlasVersion;

            string sourcePath = WetplatePhotoSync.GetPhotoPath(photoFileName);
            if (!File.Exists(sourcePath))
            {
                try
                {
                    CollodionModSystem.ClientInstance?.PhotoSync?.ClientRequestPhotoIfMissing(photoFileName);
                    if (waitingPos != null)
                    {
                        CollodionModSystem.ClientInstance?.PhotoSync?.ClientNoteBlockWaitingForPhoto(photoFileName, waitingPos);
                    }
                }
                catch { }
                return false;
            }

            string renderPath = sourcePath;
            string renderFileName = photoFileName;
            if (!string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase))
            {
                string derivedFileName = GetDerivedPhotoFileName(photoFileName, $"developed{developPours}");
                string derivedPath = GetDerivedPhotoPath(photoFileName, $"developed{developPours}");

                if (TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|developed|{developPours}", developPours))
                {
                    renderPath = derivedPath;
                    renderFileName = derivedFileName;
                }
            }

            try
            {
                byte[] pngBytes = File.ReadAllBytes(renderPath);

                photoAspect = 1f;
                if (TryGetPngDimensions(pngBytes, out int pngW, out int pngH) && pngH > 0)
                {
                    photoAspect = pngW / (float)pngH;
                }

                string photoKey = Path.GetFileNameWithoutExtension(renderFileName);
                AssetLocation texLoc = new AssetLocation("collodion", $"photo-block-{photoKey}-v{versionSnapshot}");

                capi.BlockTextureAtlas.GetOrInsertTexture(
                    texLoc,
                    out int _,
                    out texPos,
                    () => capi.Render.BitmapCreateFromPng(pngBytes),
                    0.05f
                );
                return texPos != null && texPos != capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render tray photo overlay: " + ex);
            }

            return false;
        }

        private static MeshData CreateFrontOverlayQuad(TextureAtlasPosition texPos, MeshData baseMesh, int uvRotationDeg, bool mirrorX, float photoAspect)
        {
            // Derive the photo quad from the current plate model bounds so shape changes
            // (e.g. recent re-centering for GUI spin) don't break placement.
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            float[] xyz = baseMesh.xyz ?? Array.Empty<float>();
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

            // Center-crop the photo so it fills the plate without stretching.
            ApplyUvCenterCropToAspect(m, photoAspect, PhotoTargetAspect, uvRotationDeg);

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

        private static void ApplyUvCenterCropToAspect(MeshData mesh, float sourceAspect, float targetAspect, int rotationDeg)
        {
            if (mesh.Uv == null || mesh.VerticesCount <= 0) return;
            if (sourceAspect <= 0 || targetAspect <= 0) return;

            int rot = ((rotationDeg % 360) + 360) % 360;
            bool rot90 = rot == 90 || rot == 270;

            float effectiveSourceAspect = rot90 ? (1f / sourceAspect) : sourceAspect;
            if (effectiveSourceAspect <= 0) return;

            float keepU = 1f;
            float keepV = 1f;

            // If the source is wider than the target, crop left/right (U). Otherwise crop top/bottom (V).
            if (effectiveSourceAspect > targetAspect)
            {
                keepU = targetAspect / effectiveSourceAspect;
            }
            else
            {
                keepV = effectiveSourceAspect / targetAspect;
            }

            if (keepU < 0f) keepU = 0f;
            if (keepU > 1f) keepU = 1f;
            if (keepV < 0f) keepV = 0f;
            if (keepV > 1f) keepV = 1f;

            float uMin = (1f - keepU) * 0.5f;
            float vMin = (1f - keepV) * 0.5f;

            float[] uv = mesh.Uv;
            for (int i = 0; i < mesh.VerticesCount; i++)
            {
                uv[i * 2 + 0] = uMin + uv[i * 2 + 0] * keepU;
                uv[i * 2 + 1] = vMin + uv[i * 2 + 1] * keepV;
            }
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

        private static string GetDerivedPhotoFileName(string photoFileName, string profile)
        {
            string baseName = Path.GetFileNameWithoutExtension(photoFileName);
            return $"{baseName}__{profile}.png";
        }

        private static string GetDerivedPhotoPath(string photoFileName, string profile)
        {
            string derivedFileName = GetDerivedPhotoFileName(photoFileName, profile);
            return Path.Combine(GamePaths.DataPath, "ModData", "collodion", "photos", "derived", derivedFileName);
        }

        private static bool TryEnsureDerivedPhoto(ICoreClientAPI capi, string sourcePath, string derivedPath, string seedKey, int developPours)
        {
            try
            {
                if (File.Exists(derivedPath))
                {
                    try
                    {
                        DateTime srcTime = File.GetLastWriteTimeUtc(sourcePath);
                        DateTime dstTime = File.GetLastWriteTimeUtc(derivedPath);
                        if (dstTime >= srcTime) return true;
                    }
                    catch
                    {
                        // If time checks fail, fall through and re-generate.
                    }
                }

                using var src = SKBitmap.Decode(sourcePath);
                if (src == null) return false;

                float t = DevelopPoursRequired <= 1 ? 1f : (developPours - 1) / (float)(DevelopPoursRequired - 1);
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;

                if (t < 0.999f)
                {
                    ApplyDevelopmentStageVisuals(src, t);
                }

                using var image = SKImage.FromBitmap(src);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);

                Directory.CreateDirectory(Path.GetDirectoryName(derivedPath)!);
                File.WriteAllBytes(derivedPath, data.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                capi.Logger.Warning($"Collodion: failed to build derived photo '{derivedPath}': {ex.Message}");
                return false;
            }
        }

        private static bool TryGetPngDimensions(byte[] pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (pngBytes == null || pngBytes.Length < 24) return false;

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

        private static void ApplyDevelopmentStageVisuals(SKBitmap bmp, float t)
        {
            if (bmp == null) return;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            // Underdeveloped look at t=0.
            float opacity = Lerp(0.15f, 1f, t);
            float contrast = Lerp(0.35f, 1f, t);
            float whiteHaze = Lerp(0.75f, 0f, t);
            float blackPoint = Lerp(0.35f, 0f, t);
            float edgeFade = Lerp(0.6f, 0f, t);

            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);

                    float r = c.Red / 255f;
                    float g = c.Green / 255f;
                    float b = c.Blue / 255f;

                    // Lift shadows (black point).
                    r = blackPoint + r * (1f - blackPoint);
                    g = blackPoint + g * (1f - blackPoint);
                    b = blackPoint + b * (1f - blackPoint);

                    // Contrast around mid-gray.
                    r = 0.5f + (r - 0.5f) * contrast;
                    g = 0.5f + (g - 0.5f) * contrast;
                    b = 0.5f + (b - 0.5f) * contrast;

                    // White haze overlay.
                    r = r + (1f - r) * whiteHaze;
                    g = g + (1f - g) * whiteHaze;
                    b = b + (1f - b) * whiteHaze;

                    // Edge fade (reduces edge artifacts early in development).
                    if (edgeFade > 0f)
                    {
                        float nx = (x + 0.5f) / w - 0.5f;
                        float ny = (y + 0.5f) / h - 0.5f;
                        float dist = (float)System.Math.Sqrt(nx * nx + ny * ny);
                        float edge = dist / 0.7071f; // 0 at center, ~1 at corner
                        if (edge > 1f) edge = 1f;
                        float fade = edge * edgeFade;
                        r = r + (1f - r) * fade;
                        g = g + (1f - g) * fade;
                        b = b + (1f - b) * fade;
                    }

                    // Low opacity (blend toward white).
                    r = r + (1f - r) * (1f - opacity);
                    g = g + (1f - g) * (1f - opacity);
                    b = b + (1f - b) * (1f - opacity);

                    byte rr = (byte)(Clamp01(r) * 255f);
                    byte gg = (byte)(Clamp01(g) * 255f);
                    byte bb = (byte)(Clamp01(b) * 255f);

                    bmp.SetPixel(x, y, new SKColor(rr, gg, bb, 255));
                }
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
