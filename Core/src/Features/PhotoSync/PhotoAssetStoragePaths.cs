using SkiaSharp;
using Vintagestory.API.Config;

namespace Photocore.PhotoSync.Storage
{
    internal static class PhotoAssetStoragePaths
    {
        internal static string NormalizePhotoId(string photoId)
        {
            if (string.IsNullOrWhiteSpace(photoId)) return string.Empty;

            string trimmed = photoId.Trim();

            // Early reject for absurdly long inputs before allocation-heavy path operations.
            if (trimmed.Length > 255) return string.Empty;

            // Reject null bytes — Path.GetFileName throws on them in .NET 6+.
            if (trimmed.IndexOf('\0') >= 0) return string.Empty;

            // Reject anything containing path separators rather than silently stripping them.
            if (trimmed.IndexOfAny(['/', '\\', ':']) >= 0) return string.Empty;

            string fileName = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            if (fileName == "." || fileName == "..") return string.Empty;
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return string.Empty;

            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";

            // Final length check after extension is appended — filesystem limit is 255 chars.
            if (fileName.Length > 255) return string.Empty;

            return fileName;
        }

        internal static string GetPhotoPath(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            return Path.Combine(GetPhotosDirectory(), normalized);
        }

        // Root directory holding the ground-truth source photos. Derived silver-density masks and
        // human-viewable exports live in the derived/ and exports/ subdirectories beneath it.
        internal static string GetPhotosDirectory()
            => Path.Combine(GamePaths.DataPath, "ModData", "photocore", "photos");

        internal static string GetDerivedDirectory()
            => Path.Combine(GetPhotosDirectory(), "derived");

        // Enumerates the normalized ids (file names) of every ground-truth source photo on disk.
        // Top-level only — the derived/ and exports/ subdirectories are intentionally excluded.
        internal static IReadOnlyList<string> EnumeratePhotoIds()
        {
            string dir = GetPhotosDirectory();
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            var ids = new List<string>();
            try
            {
                foreach (string path in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
                    ids.Add(Path.GetFileName(path));
            }
            catch
            {
                // Best-effort: a transient IO error yields whatever was enumerated so far.
            }
            return ids;
        }

        internal static long GetPhotoSizeBytes(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return 0;
            try
            {
                var info = new FileInfo(Path.Combine(GetPhotosDirectory(), normalized));
                return info.Exists ? info.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        // Last-write time (UTC) of a source photo, or null when missing/unreadable. Used as the
        // grace-period fallback for never-seen files (which have no FirstSeenUtc index row).
        internal static DateTime? GetPhotoModifiedUtc(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return null;
            try
            {
                string path = Path.Combine(GetPhotosDirectory(), normalized);
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
            }
            catch
            {
                return null;
            }
        }

        // Deletes a source photo and all of its derived mask variants (derived/<base>__*.png).
        // Returns true when the source file existed and was removed. Derived deletion is best-effort
        // so a locked/missing mask never blocks reclaiming the source.
        internal static bool DeletePhotoAndDerived(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return false;

            bool removed = false;
            try
            {
                string sourcePath = Path.Combine(GetPhotosDirectory(), normalized);
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                    removed = true;
                }
            }
            catch
            {
                // Best-effort: report not-removed; caller keeps the index row so a retry is possible.
            }

            try
            {
                string derivedDir = GetDerivedDirectory();
                if (Directory.Exists(derivedDir))
                {
                    string baseName = Path.GetFileNameWithoutExtension(normalized);
                    foreach (string path in Directory.EnumerateFiles(derivedDir, baseName + "__*.png", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(path); } catch { /* best-effort per-file */ }
                    }
                }
            }
            catch
            {
                // Best-effort derived cleanup; a leftover mask is harmless and re-derives from source.
            }

            return removed;
        }

        // Human-viewable exported composites live in photos/exports/. The caller supplies a
        // friendly base name (e.g. caption + timestamp); we make it filesystem-safe and ensure
        // a .png extension. The directory is created lazily by the writer, mirroring GetPhotoPath.
        internal static string GetExportPath(string fileName)
        {
            string safe = SanitizeExportFileName(fileName);
            return Path.Combine(GamePaths.DataPath, "ModData", "photocore", "photos", "exports", safe);
        }

        // Strips path separators and invalid filename characters, caps length, and guarantees a
        // non-empty ".png" result so an arbitrary caption can never produce an invalid path.
        internal static string SanitizeExportFileName(string fileName)
        {
            string baseName = string.IsNullOrWhiteSpace(fileName) ? "photo" : fileName.Trim();

            int dot = baseName.LastIndexOf('.');
            if (dot >= 0 && baseName.Substring(dot).Equals(".png", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, dot);

            char[] chars = baseName.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ')
                    chars[i] = '_';
            }
            baseName = new string(chars).Trim('_', '.');
            if (string.IsNullOrEmpty(baseName)) baseName = "photo";
            if (baseName.Length > 120) baseName = baseName.Substring(0, 120);

            return baseName + ".png";
        }

        // Generates a timestamped unique filename, encodes bitmap as PNG, writes it to the photo store, and returns the file name.
        internal static string SaveExposurePng(SKBitmap bitmap)
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string rnd = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            string fileName = $"exposure_{now}_{rnd}.png";
            string fullPath = GetPhotoPath(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var finalImage = SKImage.FromBitmap(bitmap);
            using var pngData = finalImage.Encode(SKEncodedImageFormat.Png, 90);
            using var output = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            pngData.SaveTo(output);
            return fileName;
        }
    }
}
