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

                bool hasCachedAspect;
                // Reuse aspect data to avoid repeated PNG header/bitmap reads.
                lock (_cacheLock)
                {
                    hasCachedAspect = _blockPhotoAspectCache.TryGetValue(renderPath, out float cachedAspect);
                    if (hasCachedAspect)
                    {
                        photoAspect = cachedAspect;
                    }
                }

                if (!hasCachedAspect)
                {
                    pngBytesForInsert = File.ReadAllBytes(renderPath);

                    photoAspect = 1f;
                    if (PhotoImageProcessor.TryGetPngDimensions(pngBytesForInsert, out int pngW, out int pngH) && pngH > 0)
                    {
                        photoAspect = pngW / (float)pngH;
                    }

                    lock (_cacheLock)
                    {
                        _blockPhotoAspectCache[renderPath] = photoAspect;
                    }
                }

                string photoKey = Path.GetFileNameWithoutExtension(renderFileName);
                AssetLocation texLoc = new AssetLocation("photocore", $"photo-block-{photoKey}-v{versionSnapshot}");
                
                // Lazily create atlas bitmap payload only when cache lookup misses.
                capi.BlockTextureAtlas.GetOrInsertTexture(
                    texLoc,
                    out int _,
                    out texPos,
                    () => capi.Render.BitmapCreateFromPng(pngBytesForInsert ?? File.ReadAllBytes(renderPath)),
                    0.05f
                );
                return texPos != null && texPos != capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render tray photo overlay: " + ex);
            }

            return false;
        }
    }
}

