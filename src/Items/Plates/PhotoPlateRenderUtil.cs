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
        private static readonly Dictionary<string, float> BlockPhotoAspectCache = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DerivedPruneState = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                BlockPhotoAspectCache.Clear();
                DerivedPruneState.Clear();
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

            string overlayFace = "south";
            try
            {
                overlayFace = itemstack.Collectible?.Attributes?["photoOverlayFace"]?.AsString("south") ?? "south";
            }
            catch
            {
                overlayFace = "south";
            }

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
                CollodionModSystem.ClientInstance?.ClientMaybeSendPhotoSeen(photoId);
            }
            catch (Exception ex)
            {
                capi.Logger.Warning("[Collodion] TryRenderPhotoOverlay photo-seen notification failed: {0}", ex.Message);
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
                // Default behavior: mirror only in GUI, unless explicitly overridden by item attributes.
                bool defaultMirrorX = target == EnumItemRenderTarget.Gui;
                mirrorX = itemstack.Collectible?.Attributes?["photoMirrorX"]?.AsBool(defaultMirrorX) ?? defaultMirrorX;
            }
            catch
            {
                mirrorX = target == EnumItemRenderTarget.Gui;
            }

            int maxDeveloperPours = itemstack.Attributes?.GetInt(WetPlateAttrs.DeveloperPourCountMax, 5) ?? 5;
            int developPours = maxDeveloperPours;
            if (!string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    developPours = itemstack.Attributes?.GetInt(WetPlateAttrs.DevelopPours, maxDeveloperPours) ?? maxDeveloperPours;
                }
                catch
                {
                    developPours = maxDeveloperPours;
                }

                if (developPours < 0) developPours = 0;
                if (developPours > maxDeveloperPours) developPours = maxDeveloperPours;
            }

            float movementScore = GetMovementScore(itemstack);
            bool hasMovementEffects = movementScore > PhotoImageProcessor.MovementEffectMin;
            int movementCacheBucket = QuantizeMovementScore(movementScore);

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

                cacheKey = $"{photoFileName}|{variant}|r{((uvRotationDeg % 360) + 360) % 360}|mx{(mirrorX ? 1 : 0)}|fx{effectsProfile}|dp{developPours}|mv{movementCacheBucket}|v{versionSnapshot}";
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
            bool useDevelopedStage = !string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase);
            MaybePruneObsoleteDevelopedDerived(capi, photoFileName, itemstack, developPours, maxDeveloperPours, useDevelopedStage);
            if (useDevelopedStage || hasMovementEffects)
            {
                string profileTag = useDevelopedStage
                    ? $"developed{developPours}"
                    : "base";

                if (hasMovementEffects)
                {
                    profileTag = $"{profileTag}-mv{movementCacheBucket}";
                }

                string derivedFileName = GetDerivedPhotoFileName(photoFileName, profileTag);
                string derivedPath = GetDerivedPhotoPath(photoFileName, profileTag);

                if (PhotoImageProcessor.TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|{profileTag}", useDevelopedStage, developPours, maxDeveloperPours, movementScore))
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

                    // Add a thin overlay quad on the configured face of the plate shape.
                    string overlayFaceNorm = (overlayFace ?? "south").Trim().ToLowerInvariant();
                    if (overlayFaceNorm == "both")
                    {
                        MeshData overlaySouth = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, "south");
                        MeshData overlayNorth = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, "north");
                        baseMesh.AddMeshData(overlaySouth);
                        baseMesh.AddMeshData(overlayNorth);
                    }
                    else
                    {
                        MeshData overlay = PhotoMeshUtil.CreateOverlayQuad(texPos, baseMesh, uvRotationDeg, mirrorX, photoAspect, overlayFaceNorm);
                        baseMesh.AddMeshData(overlay);
                    }

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
                CollodionModSystem.ClientInstance?.ClientMaybeSendPhotoSeen(photoId);
            }
            catch (Exception ex)
            {
                capi.Logger.Warning("[Collodion] TryGetPhotoBlockTexture photo-seen notification failed: {0}", ex.Message);
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

            int maxDeveloperPours = itemstack.Attributes?.GetInt(WetPlateAttrs.DeveloperPourCountMax, 5) ?? 5;
            int developPours = maxDeveloperPours;
            if (!string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    developPours = itemstack.Attributes?.GetInt(WetPlateAttrs.DevelopPours, maxDeveloperPours) ?? maxDeveloperPours;
                }
                catch
                {
                    developPours = maxDeveloperPours;
                }

                if (developPours < 0) developPours = 0;
                if (developPours > maxDeveloperPours) developPours = maxDeveloperPours;
            }

            float movementScore = GetMovementScore(itemstack);
            bool hasMovementEffects = movementScore > PhotoImageProcessor.MovementEffectMin;
            int movementCacheBucket = QuantizeMovementScore(movementScore);

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
            bool useDevelopedStage = !string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase);
            MaybePruneObsoleteDevelopedDerived(capi, photoFileName, itemstack, developPours, maxDeveloperPours, useDevelopedStage);
            if (useDevelopedStage || hasMovementEffects)
            {
                string profileTag = useDevelopedStage
                    ? $"developed{developPours}"
                    : "base";

                if (hasMovementEffects)
                {
                    profileTag = $"{profileTag}-mv{movementCacheBucket}";
                }

                string derivedFileName = GetDerivedPhotoFileName(photoFileName, profileTag);
                string derivedPath = GetDerivedPhotoPath(photoFileName, profileTag);

                if (PhotoImageProcessor.TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|{profileTag}", useDevelopedStage, developPours, maxDeveloperPours, movementScore))
                {
                    renderPath = derivedPath;
                    renderFileName = derivedFileName;
                }
            }

            try
            {
                byte[]? pngBytesForInsert = null;

                bool hasCachedAspect;
                lock (CacheLock)
                {
                    hasCachedAspect = BlockPhotoAspectCache.TryGetValue(renderPath, out float cachedAspect);
                    if (hasCachedAspect)
                    {
                        photoAspect = cachedAspect;
                    }
                }

                if (!hasCachedAspect)
                {
                    pngBytesForInsert = File.ReadAllBytes(renderPath);

                    photoAspect = 1f;
                    if (PhotoImageProcessor.TryGetPngDimensions(pngBytesForInsert, out int pngW, out int pngH) && pngH > 0)
                    {
                        photoAspect = pngW / (float)pngH;
                    }

                    lock (CacheLock)
                    {
                        BlockPhotoAspectCache[renderPath] = photoAspect;
                    }
                }

                string photoKey = Path.GetFileNameWithoutExtension(renderFileName);
                AssetLocation texLoc = new AssetLocation("collodion", $"photo-block-{photoKey}-mv{movementCacheBucket}-v{versionSnapshot}");

                capi.BlockTextureAtlas.GetOrInsertTexture(
                    texLoc,
                    out int _,
                    out texPos,
                    () => capi.Render.BitmapCreateFromPng(pngBytesForInsert ?? File.ReadAllBytes(renderPath)),
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

        private static void MaybePruneObsoleteDevelopedDerived(ICoreClientAPI capi, string photoFileName, ItemStack? itemstack, int developPours, int maxDeveloperPours, bool useDevelopedStage)
        {
            if (string.IsNullOrWhiteSpace(photoFileName) || itemstack?.Attributes == null) return;

            bool isFinishedStage = PlateStateService.GetStage(itemstack) == PlateStage.Finished;

            int keepDevelopedStage = useDevelopedStage ? developPours : 0;
            if (keepDevelopedStage < 0) keepDevelopedStage = 0;
            if (keepDevelopedStage > maxDeveloperPours) keepDevelopedStage = maxDeveloperPours;

            string pruneKey = $"{photoFileName}|{(isFinishedStage ? "finished" : "active")}|{keepDevelopedStage}";
            lock (CacheLock)
            {
                if (!DerivedPruneState.Add(pruneKey)) return;
            }

            try
            {
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "collodion", "photos", "derived");
                if (!Directory.Exists(derivedDir)) return;

                string baseName = Path.GetFileNameWithoutExtension(photoFileName);
                if (string.IsNullOrWhiteSpace(baseName)) return;

                if (isFinishedStage)
                {
                    for (int stageIndex = 1; stageIndex <= maxDeveloperPours; stageIndex++)
                    {
                        DeleteDerivedDevelopedStageFiles(derivedDir, baseName, stageIndex, maxDeveloperPours);
                    }
                    return;
                }

                if (keepDevelopedStage <= 1) return;

                int previousStage = keepDevelopedStage - 1;
                DeleteDerivedDevelopedStageFiles(derivedDir, baseName, previousStage, maxDeveloperPours);
            }
            catch (Exception ex)
            {
                capi?.Logger?.VerboseDebug($"Collodion: derived prune skipped for '{photoFileName}': {ex.Message}");
            }
        }

        private static void DeleteDerivedDevelopedStageFiles(string derivedDir, string baseName, int stageIndex, int maxDeveloperPours)
        {
            if (stageIndex < 1 || stageIndex > maxDeveloperPours) return;

            string pattern = $"{baseName}__developed{stageIndex}*.png";
            foreach (string filePath in Directory.EnumerateFiles(derivedDir, pattern, SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(filePath); } catch { }
            }
        }

        /// <summary>Returns the raw movement score from an item stack (0 if absent).</summary>
        public static float ReadMovementScore(ItemStack? itemstack) => GetMovementScore(itemstack);

        /// <summary>Returns an integer bucket for a movement score, suitable for inclusion in cache keys.</summary>
        public static int BucketMovementScore(float movementScore) => QuantizeMovementScore(movementScore);

        /// <summary>
        /// Returns the file path to render for the given photo, generating a motion-artifact derived
        /// file when the movement score is significant.  Returns <paramref name="sourcePath"/> unchanged
        /// when movement is below the effect threshold.
        /// </summary>
        public static string ResolveMovementRenderPath(ICoreClientAPI capi, string sourcePath, string photoFileName, string photoId, float movementScore)
        {
            if (movementScore <= PhotoImageProcessor.MovementEffectMin) return sourcePath;
            int bucket = QuantizeMovementScore(movementScore);
            string tag = $"base-mv{bucket}";
            string derivedPath = GetDerivedPhotoPath(photoFileName, tag);
            return PhotoImageProcessor.TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|{tag}", false, 0, 1, movementScore)
                ? derivedPath
                : sourcePath;
        }

        private static float GetMovementScore(ItemStack? itemstack)
        {
            if (itemstack?.Attributes == null) return 0f;

            try
            {
                double movement = itemstack.Attributes.GetDouble(WetPlateAttrs.HoldStillMovement, 0);
                if (movement <= 0) return 0f;
                if (movement > 1000) movement = 1000;
                return (float)movement;
            }
            catch
            {
                return 0f;
            }
        }

        private static int QuantizeMovementScore(float movementScore)
        {
            float clamped = movementScore;
            if (clamped < 0f) clamped = 0f;
            if (clamped > 1000f) clamped = 1000f;
            return (int)Math.Round(clamped * 100f);
        }

    }
}
