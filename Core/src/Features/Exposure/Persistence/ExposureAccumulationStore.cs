using System.IO.Compression;
using Vintagestory.API.Config;

namespace Collodion.Exposure
{
    /// <summary>
    /// Persists and restores raw exposure accumulation blobs between game sessions.
    /// Files are keyed by exposure ID and stored under the mod's data folder so they
    /// survive server restarts, logouts, and game relaunches.
    /// Each file is a self-describing binary blob produced by <see cref="GpuExposureAccumulator.SerializeAccumulation"/>,
    /// compressed with GZip.
    /// </summary>
    internal static class ExposureAccumulationStore
    {
        private const string FolderName = "partialexposures"; 
        private const string Extension  = ".pex";

        /// <summary>Returns the absolute path to the <c>.pex</c> file for the given exposure ID.</summary>
        internal static string GetStorePath(string exposureId)
        {
            string safeId = Path.GetFileName(exposureId.Trim());
            return Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", FolderName, safeId + Extension);
        }

        /// <summary>
        /// Compresses the serialized accumulation blob with GZip and writes it to disk.
        /// Returns <see langword="false"/> if the write fails (disk full, permissions, etc.); the caller should warn the player.
        /// </summary>
        internal static bool Save(string exposureId, byte[] data)
        {
            if (string.IsNullOrEmpty(exposureId) || data.Length == 0) return true;

            string path = GetStorePath(exposureId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                using GZipStream gz = new GZipStream(fs, CompressionLevel.Optimal);
                gz.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn(null, $"photochemistry: failed to save partial exposure '{exposureId}': {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads and decompresses a previously saved blob.
        /// Returns <see langword="false"/> when no file exists for this ID, or if the file is corrupt or unreadable.
        /// </summary>
        internal static bool TryLoad(string exposureId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? data)
        {
            data = null;
            if (string.IsNullOrEmpty(exposureId)) return false;

            string path = GetStorePath(exposureId);
            if (!File.Exists(path)) return false;

            try
            {
                using FileStream fs   = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using GZipStream gz   = new GZipStream(fs, CompressionMode.Decompress);
                using MemoryStream ms = new MemoryStream();
                gz.CopyTo(ms);
                data = ms.ToArray();
                return data.Length > 0;
            }
            catch (Exception e)
            {
                Log.Warn(null, $"photochemistry: partial exposure '{exposureId}' is corrupt or unreadable — starting fresh: {e.Message}");
                return false;
            }
        }

        /// <summary>Returns all exposure IDs that currently have a saved <c>.pex</c> file on disk.</summary>
        internal static IReadOnlyList<string> EnumerateIds()
        {
            string folder = Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", FolderName);
            if (!Directory.Exists(folder)) return Array.Empty<string>();

            string[] files = Directory.GetFiles(folder, "*" + Extension);
            var ids = new List<string>(files.Length);
            foreach (string f in files)
            {
                string? id = Path.GetFileNameWithoutExtension(f);
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>Deletes the saved partial for this exposure. Called after successful development or on expiry.</summary>
        internal static void Delete(string exposureId)
        {
            if (string.IsNullOrEmpty(exposureId)) return;
            try { File.Delete(GetStorePath(exposureId)); }
            catch (Exception e) { Log.Warn(null, $"photochemistry: could not delete partial exposure '{exposureId}': {e.Message}"); }
        }
    }
}
