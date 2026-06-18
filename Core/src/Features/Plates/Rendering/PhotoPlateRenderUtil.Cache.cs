using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Collodion.Plates.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        // Shared mesh render cache for the plate render path (item overlay + block overlay).
        private static readonly PhotoMeshRenderCache _meshRenderCache = new();

        // Auxiliary cache lock guards aspect-ratio and prune-state entries only.
        // Mesh cache concurrency is handled internally by PhotoMeshRenderCache.
        private static readonly object _cacheLock = new();
        private static readonly Dictionary<string, float> _blockPhotoAspectCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _derivedPruneState = new(StringComparer.OrdinalIgnoreCase);

        // Disposes cached mesh refs, clears render caches, and bumps versioned cache keys.
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
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", "photos", "derived");
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

        // Builds a deterministic derived image filename for a profile variant.
        private static string GetDerivedPhotoFileName(string photoFileName, string profile)
        {
            string baseName = Path.GetFileNameWithoutExtension(photoFileName);
            return $"{baseName}__{profile}.png";
        }

        // Builds the full on-disk path for a derived image variant.
        private static string GetDerivedPhotoPath(string photoFileName, string profile)
        {
            string derivedFileName = GetDerivedPhotoFileName(photoFileName, profile);
            return Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", "photos", "derived", derivedFileName);
        }

        // Best-effort cleanup of obsolete developed-stage variants for a photo.
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
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", "photos", "derived");
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
                capi?.Logger?.VerboseDebug($"photochemistry: derived prune skipped for '{photoFileName}': {ex.Message}");
            }
        }

        // Sets the render pass on every quad in a mesh to Transparent so alpha blending applies.
        private static void SetTransparentRenderPass(MeshData mesh)
        {
            if (mesh == null) return;
            int quadCount = mesh.VerticesCount / 4;
            if (quadCount <= 0) return;
            short passVal = (short)(ushort)EnumChunkRenderPass.Transparent;
            short[] passes = mesh.RenderPassesAndExtraBits;
            if (passes == null || passes.Length < quadCount)
                passes = new short[quadCount];
            for (int qi = 0; qi < quadCount; qi++)
                passes[qi] = passVal;
            mesh.RenderPassesAndExtraBits = passes;
            mesh.RenderPassCount = quadCount;
        }

        // Deletes the derived negative file for a single stage index.
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
            string photoId,
            string photoFileName,
            string sourcePath,
            string effectsProfile,
            ItemStack? itemstack,
            int developPours,
            int maxDeveloperPours,
            out string renderPath,
            out string renderFileName)
        {
            renderPath = sourcePath;
            renderFileName = photoFileName;

            bool useDevelopedStage = !string.IsNullOrWhiteSpace(effectsProfile)
                && effectsProfile.Equals("negative", StringComparison.OrdinalIgnoreCase);

            MaybePruneObsoleteDevelopedDerived(capi, photoFileName, itemstack, developPours, maxDeveloperPours, useDevelopedStage);

            if (!useDevelopedStage) return;

            string profileTag = useDevelopedStage ? $"negative{developPours}" : "base";
            string derivedFileName = GetDerivedPhotoFileName(photoFileName, profileTag);
            string derivedPath = GetDerivedPhotoPath(photoFileName, profileTag);

            if (PhotoImageProcessor.TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|{profileTag}", useDevelopedStage, developPours, maxDeveloperPours))
            {
                renderPath = derivedPath;
                renderFileName = derivedFileName;
            }
        }
    }
}

