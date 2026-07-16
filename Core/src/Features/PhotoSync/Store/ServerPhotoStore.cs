namespace Photocore.PhotoSync.Store
{
    // Server-side IPhotoStore: the server always holds (or definitively lacks) the canonical file,
    // so every call resolves off local disk — no network round trip is possible here. The read is
    // still async so callers on the main server thread never block on disk IO (matching the
    // off-thread read pattern in PhotoAssetSync.Server.cs).
    internal sealed class ServerPhotoStore : PhotoStoreBase
    {
        public override async Task<PhotoFetchResult> TryGetPhotoAsync(string photoId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Fallback-aware and migrating: a consumer holding an id minted before per-world scoping
            // existed still resolves it, and the legacy file is drained into this world's folder on
            // first use (see DESIGN-photo-store-scoping.md).
            string path = PhotoAssetStoragePaths.ResolveReadPathForUse(photoId);
            if (!File.Exists(path)) return PhotoFetchResult.Missing;

            try
            {
                return PhotoFetchResult.Found(await File.ReadAllBytesAsync(path, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Best-effort: a transient IO error surfaces the same as "not present" to callers,
                // who have no way to act differently on it anyway.
                return PhotoFetchResult.Missing;
            }
        }
    }
}
