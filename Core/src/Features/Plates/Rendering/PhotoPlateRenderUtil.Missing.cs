using System.Collections.Concurrent;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Photocore.Plates.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        // Glass is the baseline fallback; heads override per medium via ResolveMissingPhotoTexture.
        private static readonly AssetLocation _defaultMissingPhotoTexture = new AssetLocation("photocore", "textures/block/photos/missing-glass.png");

        // A null result is cached too, so a genuinely absent asset isn't re-read every frame. Concurrent
        // because block tesselation may resolve placeholders off the main thread.
        private static readonly ConcurrentDictionary<string, byte[]?> _missingTextureBytes = new ConcurrentDictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);

        // Item-driven so each head supplies its own per-medium placeholder without Core referencing a
        // non-baseline asset domain; glass falls back to the bundled default.
        internal static AssetLocation ResolveMissingPhotoTexture(ItemStack? itemstack)
        {
            string? raw = itemstack?.Collectible?.Attributes?["missingPhotoTexture"]?.AsString(null);
            return string.IsNullOrWhiteSpace(raw) ? _defaultMissingPhotoTexture : new AssetLocation(raw);
        }

        private static byte[]? GetMissingTextureBytes(ICoreClientAPI capi, AssetLocation texLoc)
            => _missingTextureBytes.GetOrAdd(texLoc.ToString(), _ => capi.Assets.TryGet(texLoc)?.Data);

        // Stable, readable atlas/cache token for a placeholder asset, e.g. "photocore-missing-glass".
        private static string MissingTextureKey(AssetLocation texLoc)
            => $"{texLoc.Domain}-{Path.GetFileNameWithoutExtension(texLoc.Path)}";

        // Held-item / GUI / ground placeholder overlay. Mirrors the normal item overlay but sources the
        // head-supplied placeholder PNG, renders opaque for every medium, and skips all derived-stage processing.
        internal static bool TryRenderMissingOverlay(
            ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, AssetLocation missingTexLoc,
            int versionSnapshot, string overlayFace, int uvRotationDeg, bool mirrorX, ref ItemRenderInfo renderinfo)
        {
            string mediumKey = MissingTextureKey(missingTexLoc);

            string variant = target switch
            {
                EnumItemRenderTarget.HandTp => "hand",
                EnumItemRenderTarget.Ground => "ground",
                _ => "gui"
            };

            string cacheKey = $"missing-{mediumKey}|{variant}|r{((uvRotationDeg % 360) + 360) % 360}|mx{(mirrorX ? 1 : 0)}|v{versionSnapshot}";
            if (_meshRenderCache.TryGetCachedRender(cacheKey, out MultiTextureMeshRef? cachedMeshRef, out int cachedTextureId) && cachedMeshRef != null)
            {
                renderinfo.ModelRef = cachedMeshRef;
                renderinfo.TextureId = cachedTextureId;
                return true;
            }

            byte[]? bytes = GetMissingTextureBytes(capi, missingTexLoc);
            if (bytes == null) return false;

            float photoAspect = 1f;
            if (PhotoImageProcessor.TryGetPngDimensions(bytes, out int pngW, out int pngH) && pngH > 0)
            {
                photoAspect = pngW / (float)pngH;
            }

            IBitmap bitmap = capi.Render.BitmapCreateFromPng(bytes);
            try
            {
                // A placeholder's pixels come from a bundled asset and never change, so its content key is
                // constant and the region is allocated exactly once for the session.
                AssetLocation texLoc = new AssetLocation("photocore", $"photo-missing-{mediumKey}");
                return BuildAndCacheOverlayMesh(capi, itemstack, target, bitmap, photoAspect, texLoc, mediumKey,
                    overlayFace, uvRotationDeg, mirrorX, opaque: true, cacheKey, versionSnapshot, ref renderinfo);
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render missing-photo plate overlay: " + ex);
                return false;
            }
            finally
            {
                if (bitmap is IDisposable disposable) disposable.Dispose();
            }
        }

        // Placed-plate / tray / frame placeholder texture, inserted into the block atlas once per asset.
        internal static bool TryGetMissingBlockTexture(ICoreClientAPI capi, AssetLocation missingTexLoc, int versionSnapshot, out TextureAtlasPosition texPos, out float photoAspect)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
            photoAspect = 1f;

            byte[]? bytes = GetMissingTextureBytes(capi, missingTexLoc);
            if (bytes == null) return false;

            if (!PhotoImageProcessor.TryGetPngDimensions(bytes, out int pngW, out int pngH) || pngW <= 0 || pngH <= 0)
            {
                return false;
            }

            photoAspect = pngW / (float)pngH;

            string mediumKey = MissingTextureKey(missingTexLoc);
            AssetLocation texLoc = new AssetLocation("photocore", $"photo-block-missing-{mediumKey}");

            try
            {
                // Bundled asset: pixels never change, so the content key is constant and this allocates once.
                return PhotoAtlasTextures.TryResolve(
                    capi,
                    capi.BlockTextureAtlas,
                    texLoc,
                    mediumKey,
                    pngW,
                    pngH,
                    () => capi.Render.BitmapCreateFromPng(bytes),
                    0.05f,
                    out texPos)
                    && texPos != capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render missing-photo block overlay: " + ex);
                return false;
            }
        }

        // Shared by the real-photo and placeholder item paths so both build and cache the overlay identically.
        private static bool BuildAndCacheOverlayMesh(
            ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target,
            IBitmap bitmap, float photoAspect, AssetLocation texLoc, string contentKey,
            string overlayFace, int uvRotationDeg, bool mirrorX, bool opaque,
            string cacheKey, int versionSnapshot, ref ItemRenderInfo renderinfo)
        {
            // The bitmap is already decoded here, so hand it straight over rather than re-decoding; the
            // factory only runs again if this location needs its pixels replaced in place.
            if (!PhotoAtlasTextures.TryResolve(capi, capi.ItemTextureAtlas, texLoc, contentKey,
                    bitmap.Width, bitmap.Height, () => bitmap, 0.05f, out TextureAtlasPosition texPos,
                    ownsBitmap: false))
            {
                return false;
            }

            Item? item = itemstack.Collectible as Item;
            if (item == null) return false;

            capi.Tesselator.TesselateItem(item, out MeshData baseMesh);

            // Only non-opaque overlays move to the transparent pass: a glass plate's silver density map
            // alpha-blends, while paper positives and placeholders are opaque.
            string overlayFaceNorm = (overlayFace ?? "south").Trim().ToLowerInvariant();
            if (overlayFaceNorm == "both")
            {
                MeshData overlaySouth = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, "south");
                MeshData overlayNorth = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, "north");
                if (!opaque) { PhotoMeshUtil.SetTransparentRenderPass(overlaySouth); PhotoMeshUtil.SetTransparentRenderPass(overlayNorth); }
                baseMesh.AddMeshData(overlaySouth);
                baseMesh.AddMeshData(overlayNorth);
            }
            else
            {
                MeshData overlay = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, overlayFaceNorm);
                if (!opaque) PhotoMeshUtil.SetTransparentRenderPass(overlay);
                baseMesh.AddMeshData(overlay);
            }

            if (target == EnumItemRenderTarget.Ground)
            {
                baseMesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), GroundScale, GroundScale, GroundScale);
            }

            int atlasTextureId = texPos.atlasTextureId;
            MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(baseMesh);

            if (!_meshRenderCache.TryStore(cacheKey, versionSnapshot, meshRef, atlasTextureId))
            {
                meshRef.Dispose();
                return false;
            }

            renderinfo.ModelRef = meshRef;
            renderinfo.TextureId = atlasTextureId;
            return true;
        }
    }
}
