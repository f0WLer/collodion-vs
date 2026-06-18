using SkiaSharp;
using Vintagestory.API.Config;

namespace Photochemistry.PhotoSync.Storage
{
    // Photo id normalization and canonical on-disk photo path rules.
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
            return Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", "photos", normalized);
        }

        // Human-viewable exported composites live in photos/exports/. The caller supplies a
        // friendly base name (e.g. caption + timestamp); we make it filesystem-safe and ensure
        // a .png extension. The directory is created lazily by the writer, mirroring GetPhotoPath.
        internal static string GetExportPath(string fileName)
        {
            string safe = SanitizeExportFileName(fileName);
            return Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", "photos", "exports", safe);
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
