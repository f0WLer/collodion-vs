using System.IO.Compression;
using Vintagestory.API.Config;

namespace Photocore.Exposure
{
    // Files survive server restarts and relaunches; each blob is GZip-compressed.
    internal static class ExposureAccumulationStore
    {
        private const string FolderName = "partialexposures";
        private const string Extension  = ".pex";

        internal static string GetStorePath(string exposureId)
        {
            string safeId = Path.GetFileName(exposureId.Trim());
            return Path.Combine(GamePaths.DataPath, "ModData", "photocore", FolderName, safeId + Extension);
        }

        // Returns false on failure — caller should warn the player.
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
                Log.Warn(null, $"photocore: failed to save partial exposure '{exposureId}': {e.Message}");
                return false;
            }
        }

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
                Log.Warn(null, $"photocore: partial exposure '{exposureId}' is corrupt or unreadable — starting fresh: {e.Message}");
                return false;
            }
        }

        internal static IReadOnlyList<string> EnumerateIds()
        {
            string folder = Path.Combine(GamePaths.DataPath, "ModData", "photocore", FolderName);
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

        internal static void Delete(string exposureId)
        {
            if (string.IsNullOrEmpty(exposureId)) return;
            try { File.Delete(GetStorePath(exposureId)); }
            catch (Exception e) { Log.Warn(null, $"photocore: could not delete partial exposure '{exposureId}': {e.Message}"); }
        }
    }
}
