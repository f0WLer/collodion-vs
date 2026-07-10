using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Photocore.PhotoSync.Integration;

namespace Photocore.Plates.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        public static bool TryGetPhotoBlockTexture(ICoreClientAPI capi, ItemStack? itemstack, out TextureAtlasPosition texPos, out float photoAspect, BlockPos? waitingPos = null)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
            photoAspect = 1f;

            if (capi == null || itemstack == null) return false;

            if (!TryResolvePhotoRenderInputs(capi, itemstack, "TryGetPhotoBlockTexture", out PhotoRenderInputs inputs))
                return false;

            int versionSnapshot = inputs.VersionSnapshot;

            if (!File.Exists(inputs.SourcePath))
            {
                // A missing file is either permanently gone or still syncing: only a server-confirmed miss
                // shows the placeholder, otherwise we request the photo and keep the block waiting for it.
                if (ClientPhotoSyncIntegration.IsPhotoConfirmedMissing(capi, inputs.PhotoFileName))
                {
                    return TryGetMissingBlockTexture(capi, ResolveMissingPhotoTexture(itemstack), versionSnapshot, out texPos, out photoAspect);
                }

                try
                {
                    ClientPhotoSyncIntegration.RequestPhotoIfMissing(capi, inputs.PhotoFileName);
                    if (waitingPos != null)
                    {
                        ClientPhotoSyncIntegration.NoteBlockWaitingForPhoto(capi, inputs.PhotoFileName, waitingPos);
                    }
                }
                catch { /* intentional: PhotoSync request is best-effort; missing photo returns false and renders nothing */ }
                return false;
            }

            // Prune stale derived variants before resolving the currently active variant.
            ResolveDerivedRenderPath(capi, itemstack, inputs, out string renderPath, out string renderFileName);


            try
            {
                byte[]? pngBytesForInsert = null;

                bool hasCachedSize;
                int photoWidth, photoHeight;
                // Reuse size data to avoid repeated PNG header/bitmap reads.
                lock (_cacheLock)
                {
                    hasCachedSize = _blockPhotoSizeCache.TryGetValue(renderPath, out (int W, int H) cachedSize);
                    photoWidth = cachedSize.W;
                    photoHeight = cachedSize.H;
                }

                if (!hasCachedSize)
                {
                    pngBytesForInsert = File.ReadAllBytes(renderPath);
                    if (!PhotoImageProcessor.TryGetPngDimensions(pngBytesForInsert, out photoWidth, out photoHeight)
                        || photoWidth <= 0 || photoHeight <= 0)
                    {
                        return false;
                    }

                    lock (_cacheLock)
                    {
                        _blockPhotoSizeCache[renderPath] = (photoWidth, photoHeight);
                    }
                }

                photoAspect = photoWidth / (float)photoHeight;

                // One atlas region per photo per medium, held for the session. The developer-pour stage and
                // the atlas version live in the content key instead of the texture key, so advancing a pour
                // or re-deriving after clearcache re-uploads into this same region rather than allocating a
                // new one and orphaning the old. Glass and paper are separate regions because a plate and a
                // print of the same photo can be on screen at once, with different pixels.
                AssetLocation texLoc = BlockPhotoTextureLocation(inputs);
                string contentKey = $"{renderFileName}|v{versionSnapshot}";

                // Lazily create atlas bitmap payload only when the pixels are actually needed.
                return PhotoAtlasTextures.TryResolve(
                    capi,
                    capi.BlockTextureAtlas,
                    texLoc,
                    contentKey,
                    photoWidth,
                    photoHeight,
                    () => capi.Render.BitmapCreateFromPng(pngBytesForInsert ?? File.ReadAllBytes(renderPath)),
                    0.05f,
                    out texPos)
                    && texPos != capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render tray photo overlay: " + ex);
            }

            return false;
        }
    }
}

