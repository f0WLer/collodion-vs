using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Photochemistry.PhotoMetadata.Model;
using Photochemistry.PhotoSync.Integration;
using Photochemistry.PhotoSync.Storage;

namespace Photochemistry.Plates.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        private const float GroundScale = 2.5f;

        // Returns whether the item explicitly opts into plate photo overlay rendering.
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

        // Builds or reuses an item mesh with the resolved photo texture overlay.
        public static bool TryRenderPhotoOverlay(ICoreClientAPI capi, ItemStack? itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (capi == null || itemstack == null) return false;

            // Resolve item-driven overlay tuning attributes with safe fallbacks.
            string overlayFace = "south";
            try
            {
                overlayFace = itemstack.Collectible?.Attributes?["photoOverlayFace"]?.AsString("south") ?? "south";
            }
            catch
            {
                overlayFace = "south";
            }

            string photoId = itemstack.Attributes?.GetString(PhotographAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return false;

            // Physical medium (glass density map vs opaque paper positive), from the item's plateMedium attribute.
            PlatePresentation presentation = PlatePresentation.Resolve(itemstack);
            bool isPaper = presentation.Medium == PresentationMedium.PaperPrint;

            PlateStage stage = PlateAttributes.GetStage(itemstack);
            bool showNegative = stage == PlateStage.Developing || stage == PlateStage.Developed || stage == PlateStage.Finished;
            // The derived-image tag also separates glass ("negative") from paper ("paperprint") variants.
            string effectsProfile = showNegative ? (isPaper ? "paperprint" : "negative") : string.Empty;

            // Keep server-side last-seen metadata fresh while the photo is being rendered.
            try
            {
                ClientPhotoSyncIntegration.MaybeSendPhotoSeen(capi, photoId);
            }
            catch (Exception ex)
            {
                    Log.Debug(capi.Logger, "TryRenderPhotoOverlay photo-seen notification failed: {0}", ex.Message);
            }

            string photoFileName = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
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
                // Default behavior: mirror only in GUI, unless explicitly overridden by item attributes.
                bool defaultMirrorX = target == EnumItemRenderTarget.Gui;
                mirrorX = itemstack.Collectible?.Attributes?["photoMirrorX"]?.AsBool(defaultMirrorX) ?? defaultMirrorX;
            }
            catch
            {
                mirrorX = target == EnumItemRenderTarget.Gui;
            }

            int maxDeveloperPours = 1;
            int developPours = maxDeveloperPours;
            if (!string.IsNullOrWhiteSpace(effectsProfile))
            {
                ResolveDevelopedRenderProgress(capi, itemstack, out developPours, out maxDeveloperPours);
            }

            int versionSnapshot = _meshRenderCache.GetAtlasVersionSnapshot();

            string variant = target switch
            {
                EnumItemRenderTarget.HandTp => "hand",
                EnumItemRenderTarget.Ground => "ground",
                _ => "gui"
            };

            // Reuse existing mesh refs when the render variant key is identical.
            string cacheKey = $"{photoFileName}|{variant}|r{((uvRotationDeg % 360) + 360) % 360}|mx{(mirrorX ? 1 : 0)}|fx{effectsProfile}|dp{developPours}|v{versionSnapshot}";
            if (_meshRenderCache.TryGetCachedRender(cacheKey, out MultiTextureMeshRef? cachedMeshRef, out int cachedTextureId) && cachedMeshRef != null)
            {
                renderinfo.ModelRef = cachedMeshRef;
                renderinfo.TextureId = cachedTextureId;
                return true;
            }

            string sourcePath = PhotoAssetStoragePaths.GetPhotoPath(photoFileName);
            if (!File.Exists(sourcePath))
            {
                // Photo may still be syncing from server; request and skip render for now.
                BestEffort.Try(capi.Logger, "photo-sync-request", () => ClientPhotoSyncIntegration.RequestPhotoIfMissing(capi, photoFileName), BestEffortLogPolicy.WarnRateLimited(30000));
                return false;
            }

            // Prune stale stage variants and ensure the active derived render variant exists.
            ResolveDerivedRenderPath(capi, photoId, photoFileName, sourcePath, effectsProfile, itemstack,
                developPours, maxDeveloperPours, presentation,
                out string renderPath, out string renderFileName);


            try
            {
                // Upload texture, build overlay quad, and cache the final uploaded mesh.
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
                    AssetLocation texLoc = new AssetLocation("photochemistry", $"photo-{photoKey}-v{versionSnapshot}");

                    TextureAtlasPosition texPos;
                    int texSubId;

#pragma warning disable CS0618 // InsertTextureCached obsolete vs GetOrInsertTexture lazy-load; no gain here since bitmap is always needed for aspect ratio.
                    capi.ItemTextureAtlas.InsertTextureCached(texLoc, (IBitmap)bitmap, out texSubId, out texPos, 0.05f);
#pragma warning restore CS0618

                    Item? item = itemstack.Collectible as Item;
                    if (item == null) return false;

                    // Base mesh from the item shape and its plate texture.
                    capi.Tesselator.TesselateItem(item, out MeshData baseMesh);

                    // Add a thin overlay quad on the configured face of the plate shape.
                    // Glass plates alpha-blend the silver density map (transparent pass); a paper print
                    // is an opaque reflective positive and stays in the default opaque pass.
                    string overlayFaceNorm = (overlayFace ?? "south").Trim().ToLowerInvariant();
                    if (overlayFaceNorm == "both")
                    {
                        MeshData overlaySouth = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, "south");
                        MeshData overlayNorth = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, "north");
                        if (!isPaper) { SetTransparentRenderPass(overlaySouth); SetTransparentRenderPass(overlayNorth); }
                        baseMesh.AddMeshData(overlaySouth);
                        baseMesh.AddMeshData(overlayNorth);
                    }
                    else
                    {
                        MeshData overlay = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, overlayFaceNorm);
                        if (!isPaper) SetTransparentRenderPass(overlay);
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
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render photo plate overlay: " + ex);
            }

            return false;
        }
    }
}

