using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Photocore.PhotoMetadata.Model;
using Photocore.PhotoSync.Integration;
using Photocore.PhotoSync.Storage;

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

            string photoId = itemstack.Attributes?.GetString(PhotographAttrs.PhotoId) ?? string.Empty;
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
            string sourcePath = PhotoAssetStoragePaths.GetPhotoPath(photoFileName);

            inputs = new PhotoRenderInputs(photoId, photoFileName, presentation, isPaper, effectsProfile,
                developPours, maxDeveloperPours, versionSnapshot, sourcePath);
            return true;
        }
        private static readonly PhotoMeshRenderCache _meshRenderCache = new();

        // Auxiliary cache lock guards aspect-ratio and prune-state entries only.
        // Mesh cache concurrency is handled internally by PhotoMeshRenderCache.
        private static readonly object _cacheLock = new();
        private static readonly Dictionary<string, float> _blockPhotoAspectCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _derivedPruneState = new(StringComparer.OrdinalIgnoreCase);

        public static int ClearClientRenderCacheAndBumpVersion()
        {
            int cleared = _meshRenderCache.ClearAndBumpVersion();

            lock (_cacheLock)
            {
                _blockPhotoAspectCache.Clear();
                _derivedPruneState.Clear();
            }

            // Clear derived photo cache (best effort)
            try
            {
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "photocore", "photos", "derived");
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

        private static string GetDerivedPhotoPath(string photoFileName, string profile)
        {
            string derivedFileName = GetDerivedPhotoFileName(photoFileName, profile);
            return Path.Combine(GamePaths.DataPath, "ModData", "photocore", "photos", "derived", derivedFileName);
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
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "photocore", "photos", "derived");
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

