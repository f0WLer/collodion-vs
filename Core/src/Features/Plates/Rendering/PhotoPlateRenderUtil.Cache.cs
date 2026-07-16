using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Photocore.PhotoMetadata.Model;
using Photocore.PhotoSync.Integration;
using Photocore.PhotoSync;

namespace Photocore.Plates.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        private const int MaxDeveloperPours = 5;

        // True when the plate's itemtype declares the paper-print medium (an opaque reflective positive)
        // rather than the default silver-on-glass density map. Drives opaque-vs-transparent render choices
        // across the held item, the development tray, frames, and placed plate blocks.
        public static bool IsPaperMedium(ItemStack? stack)
            => string.Equals(
                stack?.Collectible?.Attributes?["plateMedium"]?.AsString(null),
                "paperprint", System.StringComparison.OrdinalIgnoreCase);

        // Resolves effective developer progress for render-stage visuals, clamped to process limits.
        private static void ResolveDevelopedRenderProgress(ICoreClientAPI capi, ItemStack itemstack, out int developPours, out int maxDeveloperPours)
        {
            maxDeveloperPours = MaxDeveloperPours;

            if (PlateAttributes.GetStage(itemstack) == PlateStage.Developed || PlateAttributes.GetStage(itemstack) == PlateStage.Finished)
            {
                developPours = maxDeveloperPours;
                return;
            }

            if (PlateAttributes.GetStage(itemstack) == PlateStage.Developing)
            {
                developPours = PlateAttributes.GetDevelopmentApplications(itemstack);
            }
            else
            {
                developPours = 0;
            }

            // Keep stage-based progress stable for cache keys and derived render variants.
            if (developPours < 0) developPours = 0;
            if (developPours > maxDeveloperPours) developPours = maxDeveloperPours;
        }

        // Photo render inputs resolved identically by the item-overlay and block-overlay paths.
        private readonly struct PhotoRenderInputs
        {
            internal readonly string PhotoId;
            internal readonly string PhotoFileName;
            internal readonly PlatePresentation Presentation;
            internal readonly bool IsPaper;
            internal readonly string EffectsProfile;
            internal readonly int DevelopPours;
            internal readonly int MaxDeveloperPours;
            internal readonly int VersionSnapshot;
            internal readonly string SourcePath;

            internal PhotoRenderInputs(string photoId, string photoFileName, PlatePresentation presentation, bool isPaper,
                string effectsProfile, int developPours, int maxDeveloperPours, int versionSnapshot, string sourcePath)
            {
                PhotoId = photoId;
                PhotoFileName = photoFileName;
                Presentation = presentation;
                IsPaper = isPaper;
                EffectsProfile = effectsProfile;
                DevelopPours = developPours;
                MaxDeveloperPours = maxDeveloperPours;
                VersionSnapshot = versionSnapshot;
                SourcePath = sourcePath;
            }
        }

        // Resolves the photo render inputs shared by the item and block overlay paths: the physical
        // medium/presentation, the developed-stage effects tag (glass "negative" vs paper "paperprint"),
        // the developer-pour progress, the atlas version snapshot, and the source photo path.
        // Returns false (inputs defaulted) when the stack carries no photo id or it normalizes to nothing;
        // both callers skip rendering in that case. logContext names the caller for the photo-seen log line.
        private static bool TryResolvePhotoRenderInputs(ICoreClientAPI capi, ItemStack itemstack, string logContext, out PhotoRenderInputs inputs)
        {
            inputs = default;

            string photoId = itemstack.ResolvePhotoId();
            if (string.IsNullOrEmpty(photoId)) return false;

            // Keep server-side last-seen metadata fresh while the photo is being rendered.
            try
            {
                ClientPhotoSyncIntegration.MaybeSendPhotoSeen(capi, photoId);
            }
            catch (Exception ex)
            {
                Log.Debug(capi.Logger, logContext + " photo-seen notification failed: {0}", ex.Message);
            }

            // Physical medium (glass density map vs opaque paper positive), from the item's plateMedium attribute.
            PlatePresentation presentation = PlatePresentation.Resolve(itemstack);
            bool isPaper = presentation.Medium == PresentationMedium.PaperPrint;

            PlateStage stage = PlateAttributes.GetStage(itemstack);
            bool showNegative = stage == PlateStage.Developing || stage == PlateStage.Developed || stage == PlateStage.Finished;
            // The derived-image tag also separates glass ("negative") from paper ("paperprint") variants.
            string effectsProfile = showNegative ? (isPaper ? "paperprint" : "negative") : string.Empty;

            string photoFileName = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoFileName)) return false;

            int maxDeveloperPours = 1;
            int developPours = maxDeveloperPours;
            if (!string.IsNullOrWhiteSpace(effectsProfile))
            {
                ResolveDevelopedRenderProgress(capi, itemstack, out developPours, out maxDeveloperPours);
            }

            int versionSnapshot = _meshRenderCache.GetAtlasVersionSnapshot();
            // Fallback-aware and migrating: a photo minted before per-world scoping existed still
            // renders on any frame/plate placed before the update, and drains into this world's
            // folder the first time it is rendered.
            string sourcePath = PhotoAssetStoragePaths.ResolveReadPathForUse(photoFileName);

            inputs = new PhotoRenderInputs(photoId, photoFileName, presentation, isPaper, effectsProfile,
                developPours, maxDeveloperPours, versionSnapshot, sourcePath);
            return true;
        }
        private static readonly PhotoMeshRenderCache _meshRenderCache = new();

        // Auxiliary cache lock guards photo-size and prune-state entries only.
        // Mesh cache concurrency is handled internally by PhotoMeshRenderCache.
        private static readonly object _cacheLock = new();
        private static readonly Dictionary<string, (int W, int H)> _blockPhotoSizeCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _derivedPruneState = new(StringComparer.OrdinalIgnoreCase);

        // A photo's atlas region is keyed by photo and medium only. Everything that changes its pixels
        // without changing its size — the developer-pour stage, the atlas version — belongs in the content
        // key handed to PhotoAtlasTextures, which re-uploads in place instead of allocating a new region.
        private static string PhotoTextureMedium(in PhotoRenderInputs inputs) => inputs.IsPaper ? "paper" : "glass";

        private static AssetLocation BlockPhotoTextureLocation(in PhotoRenderInputs inputs)
            => new AssetLocation("photocore", $"photo-block-{Path.GetFileNameWithoutExtension(inputs.PhotoFileName)}-{PhotoTextureMedium(inputs)}");

        private static AssetLocation ItemPhotoTextureLocation(in PhotoRenderInputs inputs)
            => new AssetLocation("photocore", $"photo-{Path.GetFileNameWithoutExtension(inputs.PhotoFileName)}-{PhotoTextureMedium(inputs)}");

        // Invalidates the render state for a single photo, without touching any other photo's.
        //
        // Prefer this over ClearClientRenderCacheAndBumpVersion for per-photo events (a download landing, a
        // photo confirmed missing). The version bump is part of every atlas texture key, so bumping it makes
        // the next render of *every* photo miss the atlas and allocate a fresh region — and nothing ever
        // hands the old region back, because the meshes of placed blocks still reference it. Photo bytes are
        // immutable once written (the server refuses to overwrite an existing photo, and derived variants
        // carry their stage in the filename), so a per-photo event never needs a global invalidation.
        public static int InvalidatePhotoRenderCache(string photoId)
        {
            string photoFileName = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoFileName)) return 0;

            int cleared = _meshRenderCache.RemoveForPhoto(photoFileName);

            string baseName = Path.GetFileNameWithoutExtension(photoFileName);
            lock (_cacheLock)
            {
                foreach (string path in _blockPhotoSizeCache.Keys
                             .Where(p => Path.GetFileName(p).StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    _blockPhotoSizeCache.Remove(path);
                }

                foreach (string key in _derivedPruneState
                             .Where(k => k.StartsWith(photoFileName + "|", StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    _derivedPruneState.Remove(key);
                }
            }

            return cleared;
        }

        // Full invalidation: every photo re-derives and re-uploads its pixels on next render. Pass capi so
        // placed blocks re-tessellate — their meshes are not in the mesh cache, and without a redraw they
        // would keep showing the pixels uploaded before the derived images were deleted below.
        //
        // Atlas regions are deliberately not freed. They are keyed by photo and medium only, so the very
        // same regions get re-used by the re-render; freeing them would additionally be unsafe, since a
        // block mesh keeps sampling its old UVs until its re-tessellation actually lands.
        public static int ClearClientRenderCacheAndBumpVersion(ICoreClientAPI? capi = null)
        {
            int cleared = _meshRenderCache.ClearAndBumpVersion();

            lock (_cacheLock)
            {
                _blockPhotoSizeCache.Clear();
                _derivedPruneState.Clear();
            }

            if (capi?.World is ClientMain clientMain) clientMain.RedrawAllBlocks();

            // Clear derived photo cache (best effort). Scoped to the current world/session -- a
            // stale derived file left in another world's folder is harmless residue, not a bug.
            try
            {
                string derivedDir = PhotoAssetStoragePaths.GetDerivedDirectory();
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

        private static string GetDerivedPhotoFileName(string photoFileName, string profile)
        {
            string baseName = Path.GetFileNameWithoutExtension(photoFileName);
            return $"{baseName}__{profile}.png";
        }

        // Always the CURRENT world's scoped derived dir, even when the source photo itself resolved
        // via the legacy fallback (TryResolveReadPath) -- derived masks are a re-derivable cache, so
        // there is no need to also maintain a separate legacy derived location.
        private static string GetDerivedPhotoPath(string photoFileName, string profile)
        {
            string derivedFileName = GetDerivedPhotoFileName(photoFileName, profile);
            return Path.Combine(PhotoAssetStoragePaths.GetDerivedDirectory(), derivedFileName);
        }

        private static void MaybePruneObsoleteDevelopedDerived(ICoreClientAPI capi, string photoFileName, ItemStack? itemstack, int developPours, int maxDeveloperPours, bool useDevelopedStage)
        {
            if (string.IsNullOrWhiteSpace(photoFileName) || itemstack?.Attributes == null) return;

            bool isFinishedStage = PlateAttributes.GetStage(itemstack) == PlateStage.Finished;

            int keepDevelopedStage = useDevelopedStage ? developPours : 0;
            if (keepDevelopedStage < 0) keepDevelopedStage = 0;
            if (keepDevelopedStage > maxDeveloperPours) keepDevelopedStage = maxDeveloperPours;

            string pruneKey = $"{photoFileName}|{(isFinishedStage ? "finished" : "active")}|{keepDevelopedStage}";
            lock (_cacheLock)
            {
                if (!_derivedPruneState.Add(pruneKey)) return;
            }

            // Pruning is intentionally soft-fail to avoid breaking render paths on IO races.
            try
            {
                string derivedDir = PhotoAssetStoragePaths.GetDerivedDirectory();
                if (!Directory.Exists(derivedDir)) return;

                string baseName = Path.GetFileNameWithoutExtension(photoFileName);
                if (string.IsNullOrWhiteSpace(baseName)) return;

                if (isFinishedStage)
                {
                    // Prune intermediate pours 1..4 only. negative5 is kept for rendering the finished plate.
                    for (int stageIndex = 1; stageIndex < maxDeveloperPours; stageIndex++)
                    {
                        DeleteDerivedNegativeStageFile(derivedDir, baseName, stageIndex);
                    }
                    return;
                }

                if (keepDevelopedStage <= 1) return;

                int previousStage = keepDevelopedStage - 1;
                DeleteDerivedNegativeStageFile(derivedDir, baseName, previousStage);
            }
            catch (Exception ex)
            {
                capi?.Logger?.VerboseDebug($"photocore: derived prune skipped for '{photoFileName}': {ex.Message}");
            }
        }

        private static void DeleteDerivedNegativeStageFile(string derivedDir, string baseName, int stageIndex)
        {
            if (stageIndex < 1) return;

            string pattern = $"{baseName}__negative{stageIndex}*.png";
            foreach (string filePath in Directory.EnumerateFiles(derivedDir, pattern, SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(filePath); } catch { /* intentional: stale derived file deletion is best-effort; locked or missing files are skipped */ }
            }
        }

        // Resolves the effective render path and filename for a photo, applying derived-stage
        // and motion-artifact variants when needed.  Prunes obsolete derived files before resolving.
        // When no derived variant applies, renderPath == sourcePath and renderFileName == photoFileName.
        private static void ResolveDerivedRenderPath(
            ICoreClientAPI capi,
            ItemStack? itemstack,
            in PhotoRenderInputs inputs,
            out string renderPath,
            out string renderFileName)
        {
            renderPath = inputs.SourcePath;
            renderFileName = inputs.PhotoFileName;

            // EffectsProfile carries the medium-specific developed tag ("negative" for glass,
            // "paperprint" for salted paper); empty means the latent (pre-developed) stage.
            bool useDevelopedStage = !string.IsNullOrWhiteSpace(inputs.EffectsProfile);

            MaybePruneObsoleteDevelopedDerived(capi, inputs.PhotoFileName, itemstack, inputs.DevelopPours, inputs.MaxDeveloperPours, useDevelopedStage);

            if (!useDevelopedStage) return;

            string profileTag = $"{inputs.EffectsProfile}{inputs.DevelopPours}";
            string derivedFileName = GetDerivedPhotoFileName(inputs.PhotoFileName, profileTag);
            string derivedPath = GetDerivedPhotoPath(inputs.PhotoFileName, profileTag);

            if (PhotoImageProcessor.TryEnsureDerivedPhoto(capi, inputs.SourcePath, derivedPath, $"{inputs.PhotoId}|{profileTag}", useDevelopedStage, inputs.DevelopPours, inputs.MaxDeveloperPours, inputs.Presentation))
            {
                renderPath = derivedPath;
                renderFileName = derivedFileName;
            }
        }
    }
}

