using Photocore.PhotoSync.Runtime;

namespace Photocore.PhotoSync.Store
{
    // Client-side IPhotoStore: local disk is only a cache here, so TryGetPhotoAsync may enqueue a
    // throttled download through the existing sync runtime and wait for its outcome.
    internal sealed class ClientPhotoStore : PhotoStoreBase
    {
        private readonly PhotoAssetSyncCore _runtime;

        internal ClientPhotoStore(PhotoAssetSyncCore runtime)
        {
            _runtime = runtime;
        }

        public override Task<PhotoFetchResult> TryGetPhotoAsync(string photoId, CancellationToken ct = default)
            => _runtime.ClientTryGetPhotoAsync(photoId, ct);
    }
}
