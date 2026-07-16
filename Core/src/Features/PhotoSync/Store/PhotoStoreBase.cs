namespace Photocore.PhotoSync.Store
{
    // Shared read-only members of the public photo store; only byte retrieval differs by side
    // (ServerPhotoStore reads authoritative disk, ClientPhotoStore waits on the sync runtime).
    internal abstract class PhotoStoreBase : IPhotoStore
    {
        public abstract Task<PhotoFetchResult> TryGetPhotoAsync(string photoId, CancellationToken ct = default);

        public bool ExistsLocally(string photoId)
        {
            string normalized = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return false;
            return File.Exists(PhotoAssetStoragePaths.TryResolveReadPath(normalized));
        }

        public string GetTypeTag(string photoId) => PhotoAssetStoragePaths.GetTypeTag(photoId);

        // Ids are canonically extensionless (the .png is storage, not identity — DESIGN-photoids),
        // so strip the storage form before handing ids out: consumers must be able to compare these
        // against ids read from item/block-entity attributes with plain string equality.
        public IReadOnlyList<string> EnumerateIds(string? typeTag = null)
        {
            IReadOnlyList<string> fileNames = PhotoAssetStoragePaths.EnumeratePhotoIds(typeTag);
            if (fileNames.Count == 0) return Array.Empty<string>();

            var ids = new string[fileNames.Count];
            for (int i = 0; i < fileNames.Count; i++)
            {
                ids[i] = Path.GetFileNameWithoutExtension(fileNames[i]);
            }
            return ids;
        }
    }
}
