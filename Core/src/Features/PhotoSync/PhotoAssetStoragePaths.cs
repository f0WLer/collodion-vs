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

        // Read fresh on every call rather than cached: on the client, SavegameIdentifier is empty
        // until the join handshake with a remote server completes, so caching a value would depend
        // on startup ordering. Set once per side in ModSystem.Server.cs / ModSystem.Client.cs.
        private static Func<string?>? _scopeIdProvider;

        internal static void SetWorldScopeIdProvider(Func<string?>? provider) => _scopeIdProvider = provider;

        // Defensive: id crosses a game-API/network boundary this code doesn't control, so a
        // malformed or spoofed value must never be able to escape the photos root. Empty result
        // means "treat as unscoped".
        internal static string SanitizeScopeFolderName(string id)
        {
            string trimmed = id.Trim();
            if (trimmed.Length == 0) return string.Empty;
            if (trimmed.IndexOfAny(['/', '\\', ':', '\0']) >= 0) return string.Empty;
            if (trimmed == "." || trimmed == "..") return string.Empty;

            char[] chars = trimmed.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }

            string safe = new string(chars);
            return safe.Length > 128 ? safe.Substring(0, 128) : safe;
        }

        private static string? ResolveWorldScopeId()
        {
            string? id = _scopeIdProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(id)) return null;
            string safe = SanitizeScopeFolderName(id);
            return safe.Length == 0 ? null : safe;
        }

        // Exposed for other per-world file names (the seen index) that must stay in lockstep with
        // the photo store's scoping.
        internal static string? GetWorldScopeIdOrNull() => ResolveWorldScopeId();

        // The un-scoped photos root. Nothing writes here anymore, but it is the fallback read
        // location for photos minted before per-world scoping existed (see TryResolveReadPath).
        private static string GetPhotosBaseDirectory()
            => Path.Combine(GamePaths.DataPath, "ModData", "photocore", "photos");

        // The current world/session's photos root. Falls back to the flat legacy root when no world
        // scope is available (defensive -- by the time any photo op runs, scoping is expected to be
        // populated).
        internal static string GetPhotosDirectory()
        {
            string? scopeId = ResolveWorldScopeId();
            string baseDir = GetPhotosBaseDirectory();
            return scopeId == null ? baseDir : Path.Combine(baseDir, scopeId);
        }

        internal static string GetDerivedDirectory()
            => Path.Combine(GetPhotosDirectory(), "derived");

        // Scoped-then-legacy, so a photo minted before per-world scoping existed still resolves
        // (old placed frames/plates keep working forever). Falls back to the scoped path -- whether
        // or not it exists -- as the canonical "this is where it belongs" location.
        internal static string TryResolveReadPath(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return string.Empty;

            string scopedPath = Path.Combine(GetPhotosDirectory(), normalized);
            if (File.Exists(scopedPath)) return scopedPath;

            string legacyPath = Path.Combine(GetPhotosBaseDirectory(), normalized);
            if (File.Exists(legacyPath)) return legacyPath;

            return scopedPath;
        }

        // Like TryResolveReadPath, but one-way migrates a legacy flat-root hit into the current
        // world's folder so pre-scoping photos become enumerable by /photoadmin and the flat root
        // drains as they are viewed. Only the world that actually references a photo migrates it,
        // and that is always the world it belongs to (its id lives in that world's block/item
        // attributes), so a flat root shared across worlds is never mis-attributed. Best-effort: a
        // move that loses a race or hits a momentarily-locked file just serves this read from the
        // legacy path and retries next time. Keep TryResolveReadPath (side-effect-free) for probes
        // and deletes; route only genuine "load the bytes to use them" sites here. Removable once
        // legacy stores have drained -- delete this and point its callers back at TryResolveReadPath.
        internal static string ResolveReadPathForUse(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return string.Empty;

            string scopedPath = Path.Combine(GetPhotosDirectory(), normalized);
            if (File.Exists(scopedPath)) return scopedPath;

            // When unscoped the scoped and legacy paths are identical, so the check above already
            // handled it; reaching here with a legacy hit means scoping is active and a move applies.
            string legacyPath = Path.Combine(GetPhotosBaseDirectory(), normalized);
            if (!File.Exists(legacyPath)) return scopedPath;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(scopedPath)!);
                File.Move(legacyPath, scopedPath);

                // Reset the mtime so the audit treats a just-migrated photo as newly arrived. mtime is
                // the audit's grace-reference fallback for files with no seen-index row (which every
                // migrated photo is, until a seen-ping records it), and the rename preserves the old
                // legacy write time -- without this a photo that was migrated *because* it is being
                // actively viewed would look like an ancient never-seen orphan and could be swept by
                // /photoadmin delete oldest|olderthan before its first ping lands.
                try { File.SetLastWriteTimeUtc(scopedPath, DateTime.UtcNow); } catch { /* best-effort */ }

                return scopedPath;
            }
            catch
            {
                return File.Exists(scopedPath) ? scopedPath : legacyPath;
            }
        }

        // World-scoped (GetPhotosDirectory) -- legacy flat-root photos are individually readable via
        // TryResolveReadPath but not enumerated here, since they can't be attributed to a world.
        // Top-level only; typeTag optionally filters to ids whose GetTypeTag matches.
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
                var info = new FileInfo(TryResolveReadPath(normalized));
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
                string path = TryResolveReadPath(normalized);
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
                string sourcePath = TryResolveReadPath(normalized);
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

        // Deliberately NOT world-scoped, so exports land in one predictable place regardless of
        // which world produced them. The caller supplies a friendly base name (e.g. caption +
        // timestamp); we make it filesystem-safe and ensure a .png extension. The directory is
        // created lazily by the writer, mirroring GetPhotoPath.
        internal static string GetExportPath(string fileName)
        {
            string safe = SanitizeExportFileName(fileName);
            return Path.Combine(GetPhotosBaseDirectory(), "exports", safe);
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
