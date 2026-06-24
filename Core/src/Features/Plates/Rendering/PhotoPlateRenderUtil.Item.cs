using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Photochemistry.PhotoSync.Integration;

namespace Photochemistry.Plates.Rendering
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

            // Item-driven overlay tuning. The null-safe attribute reads can't throw, so no guards are needed.
            string overlayFace = itemstack.Collectible?.Attributes?["photoOverlayFace"]?.AsString("south") ?? "south";
            int uvRotationDeg = itemstack.Collectible?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
            // Default behavior: mirror only in GUI, unless explicitly overridden by item attributes.
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
                // Photo may still be syncing from server; request and skip render for now.
                BestEffort.Try(capi.Logger, "photo-sync-request", () => ClientPhotoSyncIntegration.RequestPhotoIfMissing(capi, inputs.PhotoFileName), BestEffortLogPolicy.WarnRateLimited(30000));
                return false;
            }

            // Prune stale stage variants and ensure the active derived render variant exists.
            ResolveDerivedRenderPath(capi, itemstack, inputs, out string renderPath, out string renderFileName);


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
                    AssetLocation texLoc = new AssetLocation("photochemistry", $"photo-{photoKey}-v{inputs.VersionSnapshot}");

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
                        if (!isPaper) { PhotoMeshUtil.SetTransparentRenderPass(overlaySouth); PhotoMeshUtil.SetTransparentRenderPass(overlayNorth); }
                        baseMesh.AddMeshData(overlaySouth);
                        baseMesh.AddMeshData(overlayNorth);
                    }
                    else
                    {
                        MeshData overlay = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, overlayFaceNorm);
                        if (!isPaper) PhotoMeshUtil.SetTransparentRenderPass(overlay);
                        baseMesh.AddMeshData(overlay);
                    }

                    if (target == EnumItemRenderTarget.Ground)
                    {
                        baseMesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), GroundScale, GroundScale, GroundScale);
                    }

                    int atlasTextureId = texPos.atlasTextureId;
                    MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(baseMesh);

                    if (!_meshRenderCache.TryStore(cacheKey, inputs.VersionSnapshot, meshRef, atlasTextureId))
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

