using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Photocore.PhotoSync.Integration;

namespace Photocore.Plates.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        private const float GroundScale = 2.5f;

        // Builds or reuses an item mesh with the resolved photo texture overlay.
        public static bool TryRenderPhotoOverlay(ICoreClientAPI capi, ItemStack? itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (capi == null || itemstack == null) return false;

            if (!TryResolvePhotoRenderInputs(capi, itemstack, "TryRenderPhotoOverlay", out PhotoRenderInputs inputs))
                return false;

            bool isPaper = inputs.IsPaper;

            string overlayFace = itemstack.Collectible?.Attributes?["photoOverlayFace"]?.AsString("south") ?? "south";
            int uvRotationDeg = itemstack.Collectible?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
            bool defaultMirrorX = target == EnumItemRenderTarget.Gui;
            bool mirrorX = itemstack.Collectible?.Attributes?["photoMirrorX"]?.AsBool(defaultMirrorX) ?? defaultMirrorX;

            string variant = target switch
            {
                EnumItemRenderTarget.HandTp => "hand",
                EnumItemRenderTarget.Ground => "ground",
                _ => "gui"
            };

            // Reuse existing mesh refs when the render variant key is identical.
            string cacheKey = $"{inputs.PhotoFileName}|{variant}|r{((uvRotationDeg % 360) + 360) % 360}|mx{(mirrorX ? 1 : 0)}|fx{inputs.EffectsProfile}|dp{inputs.DevelopPours}|v{inputs.VersionSnapshot}";
            if (_meshRenderCache.TryGetCachedRender(cacheKey, out MultiTextureMeshRef? cachedMeshRef, out int cachedTextureId) && cachedMeshRef != null)
            {
                renderinfo.ModelRef = cachedMeshRef;
                renderinfo.TextureId = cachedTextureId;
                return true;
            }

            if (!File.Exists(inputs.SourcePath))
            {
                // A missing file is either permanently gone or still syncing: only a server-confirmed miss
                // shows the placeholder, otherwise we request the photo and skip rendering until it arrives.
                if (ClientPhotoSyncIntegration.IsPhotoConfirmedMissing(capi, inputs.PhotoFileName))
                {
                    return TryRenderMissingOverlay(capi, itemstack, target, ResolveMissingPhotoTexture(itemstack), inputs.VersionSnapshot, overlayFace, uvRotationDeg, mirrorX, ref renderinfo);
                }

                BestEffort.Try(capi.Logger, "photo-sync-request", () => ClientPhotoSyncIntegration.RequestPhotoIfMissing(capi, inputs.PhotoFileName), BestEffortLogPolicy.WarnRateLimited(30000));
                return false;
            }

            // Prune stale stage variants and ensure the active derived render variant exists.
            ResolveDerivedRenderPath(capi, itemstack, inputs, out string renderPath, out string renderFileName);


            try
            {
                // Upload texture, build overlay quad, and cache the final uploaded mesh.
                using BitmapExternal bitmap = new BitmapExternal(renderPath);

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
                AssetLocation texLoc = new AssetLocation("photocore", $"photo-{photoKey}-v{inputs.VersionSnapshot}");

                // A paper print is an opaque reflective positive; a glass plate alpha-blends its density map.
                return BuildAndCacheOverlayMesh(capi, itemstack, target, bitmap, photoAspect, texLoc,
                    overlayFace, uvRotationDeg, mirrorX, opaque: isPaper, cacheKey, inputs.VersionSnapshot, ref renderinfo);
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render photo plate overlay: " + ex);
            }

            return false;
        }
    }
}

