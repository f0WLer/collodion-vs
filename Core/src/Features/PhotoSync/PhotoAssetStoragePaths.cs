using SkiaSharp;
using Vintagestory.API.Config;

namespace Photocore.PhotoSync
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

            // "__" is reserved as the derived-file separator (derived/<base>__*.png); an id containing
            // it could cross-match another photo's derived masks in the delete/prune globs.
            if (fileName.Contains("__")) return string.Empty;

            // Lowercase is the canonical form: the seen index compares OrdinalIgnoreCase, so two ids
            // differing only in case must resolve to one file on Linux just as they do on Windows.
            fileName = fileName.ToLowerInvariant();

            if (!fileName.EndsWith(".png", StringComparison.Ordinal))
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
        // Optional typeTag filters to ids whose GetTypeTag matches (ordinal-insensitive).
        internal static IReadOnlyList<string> EnumeratePhotoIds(string? typeTag = null)
        {
            string dir = GetPhotosDirectory();
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            var ids = new List<string>();
            try
            {
                foreach (string path in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
                {
                    string id = Path.GetFileName(path);
                    if (typeTag != null && !string.Equals(GetTypeTag(id), typeTag, StringComparison.OrdinalIgnoreCase)) continue;
                    ids.Add(id);
                }
            }
            catch
            {
                // Best-effort: a transient IO error yields whatever was enumerated so far.
            }
            return ids;
        }

        // The type tag is the substring before the id's first '_' (e.g. "exposure"). Same rule for
        // both id eras. Returns empty for a malformed/untagged id rather than throwing.
        internal static string GetTypeTag(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return string.Empty;

            string baseName = Path.GetFileNameWithoutExtension(normalized);
            int underscoreIdx = baseName.IndexOf('_');
            return underscoreIdx > 0 ? baseName.Substring(0, underscoreIdx) : string.Empty;
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

        // Crockford base32 (no i, l, o, u): every character survives being read aloud, screenshotted,
        // or typed back by an admin without 0/O and 1/l confusion.
        private const string MintAlphabet = "0123456789abcdefghjkmnpqrstvwxyz";

        // The id is deliberately opaque — capture time is metadata (seen index, file mtime), never
        // identity. The tag and fixed tail length keep new ids structurally disjoint from legacy
        // timestamped ids, so the two eras can never collide. Legacy ids stay valid forever;
        // nothing outside minting may know this shape (resolution must accept both eras).
        internal static string MintExposurePhotoIdCandidate()
        {
            byte[] random = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
            Span<char> chars = stackalloc char[8];
            for (int i = 0; i < 8; i++) chars[i] = MintAlphabet[random[i] & 31];
            return "exposure_" + new string(chars);
        }

        // The returned id is extensionless — the .png is storage, not identity. Ids are minted
        // client-side without sight of the server's full store, so CreateNew atomically claims the
        // name and any pre-existing file triggers a re-mint instead of an overwrite.
        internal static string SaveExposurePng(SKBitmap bitmap)
        {
            using var finalImage = SKImage.FromBitmap(bitmap);
            using var pngData = finalImage.Encode(SKEncodedImageFormat.Png, 90);
            Directory.CreateDirectory(GetPhotosDirectory());

            for (int attempt = 0; ; attempt++)
            {
                string photoId = MintExposurePhotoIdCandidate();
                FileStream output;
                try
                {
                    output = File.Open(GetPhotoPath(photoId), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException) when (attempt < 4)
                {
                    continue;
                }

                using (output)
                {
                    pngData.SaveTo(output);
                }
                return photoId;
            }
        }
    }
}
