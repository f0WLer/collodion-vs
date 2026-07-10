using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace Photocore.Plates.Rendering
{
    // Gives each photo exactly one region in the game's texture atlas, for the whole client session.
    //
    // The atlases have no eviction: a region is held until FreeTextureSpace is called, and freeing one is
    // only safe once every mesh built against its UVs has been re-tessellated — which the game gives us no
    // way to await. So instead of allocating a new region whenever a photo's pixels change (a developer
    // pour advancing the derived variant, or .photocore clearcache re-deriving it), the region is kept and
    // the new pixels are uploaded into it. The TextureAtlasPosition never moves, so already-tessellated
    // blocks keep sampling the right rectangle and simply show the new image.
    //
    // The in-place upload is only on the concrete TextureAtlasManager, not ITextureAtlasAPI: both
    // GetOrInsertTexture and InsertTextureCached refuse to re-upload for a location they already know
    // (they only refresh when the game's own reloadIteration changes), despite what InsertTextureCached's
    // doc comment claims.
    internal static class PhotoAtlasTextures
    {
        private sealed class Entry
        {
            internal string ContentKey = string.Empty;
            internal int Width;
            internal int Height;
        }

        private static readonly object _sync = new();
        private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

        // The atlases are rebuilt by the game on world load, so every region we recorded is meaningless
        // afterwards. Callers must reset on client shutdown or the next world reuses stale dimensions.
        internal static void Reset()
        {
            lock (_sync) _entries.Clear();
        }

        // Resolves texLoc to a stable atlas region, uploading pixels only when contentKey shows the image
        // behind that location has changed. createBitmap is invoked at most once, and never on a hit where
        // the content is unchanged.
        // ownsBitmap says whether createBitmap hands over a fresh bitmap this class should dispose after an
        // in-place upload. Callers that pass a bitmap they still own must set it false — and must be on the
        // main thread, since an off-thread upload is deferred and would otherwise run after they dispose it.
        internal static bool TryResolve(
            ICoreClientAPI capi,
            ITextureAtlasAPI atlas,
            AssetLocation texLoc,
            string contentKey,
            int width,
            int height,
            CreateTextureDelegate createBitmap,
            float alphaTest,
            out TextureAtlasPosition texPos,
            bool ownsBitmap = true)
        {
            texPos = atlas.UnknownTexturePosition;
            if (width <= 0 || height <= 0) return false;

            bool isBlockAtlas = ReferenceEquals(atlas, capi.BlockTextureAtlas);
            string entryKey = (isBlockAtlas ? "b:" : "i:") + texLoc;

            Entry? entry;
            lock (_sync) _entries.TryGetValue(entryKey, out entry);

            if (entry == null)
            {
                if (!atlas.GetOrInsertTexture(texLoc, out int _, out texPos, createBitmap, alphaTest) || texPos == null)
                {
                    return false;
                }

                lock (_sync)
                {
                    _entries[entryKey] = new Entry { ContentKey = contentKey, Width = width, Height = height };
                }
                return true;
            }

            texPos = atlas[texLoc];
            if (texPos == null) return false;

            if (string.Equals(entry.ContentKey, contentKey, StringComparison.OrdinalIgnoreCase)) return true;

            // RuntimeUploadTextureToPos writes at the bitmap's own dimensions starting at the region's
            // origin, not clipped to the region — a larger image would scribble over whichever photos got
            // packed next to this one. Photo dimensions are stable per location (derived stages and the
            // effects pipeline all preserve size, and the server refuses to overwrite an existing photo),
            // so a mismatch means an assumption broke; keep the old pixels rather than corrupt a neighbour.
            if (width != entry.Width || height != entry.Height)
            {
                capi.Logger.Warning(
                    "photocore: refusing in-place atlas upload for '{0}': {1}x{2} does not match the allocated {3}x{4}.",
                    texLoc, width, height, entry.Width, entry.Height);
                return true;
            }

            if (atlas is not TextureAtlasManager manager) return true;

            // Block tessellation can run off the main thread; the upload is raw GL and must not.
            if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
            {
                capi.Event.EnqueueMainThreadTask(
                    () => UploadInPlace(capi, manager, texLoc, entryKey, contentKey, createBitmap, alphaTest, ownsBitmap),
                    "photocore-atlas-reupload");
                return true;
            }

            UploadInPlace(capi, manager, texLoc, entryKey, contentKey, createBitmap, alphaTest, ownsBitmap);
            return true;
        }

        private static void UploadInPlace(
            ICoreClientAPI capi,
            TextureAtlasManager manager,
            AssetLocation texLoc,
            string entryKey,
            string contentKey,
            CreateTextureDelegate createBitmap,
            float alphaTest,
            bool ownsBitmap)
        {
            TextureAtlasPosition? pos = manager[texLoc];
            if (pos == null) return;

            IBitmap? bitmap = null;
            try
            {
                bitmap = createBitmap();
                if (bitmap == null) return;

                manager.RuntimeUploadTextureToPos(bitmap, pos, alphaTest);

                lock (_sync)
                {
                    if (_entries.TryGetValue(entryKey, out Entry? entry)) entry.ContentKey = contentKey;
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Error("photocore: in-place atlas upload for '{0}' failed: {1}", texLoc, ex);
            }
            finally
            {
                if (ownsBitmap && bitmap is IDisposable disposable) disposable.Dispose();
            }
        }
    }
}
