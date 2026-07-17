using Photocore.PhotoMetadata;

namespace Photocore.PhotoSync.Integration
{
    internal sealed partial class PhotoSyncModSystemBridge
    {
        private ServerPhotoSeenService? _serverPhotoSeenService;
        private long? _serverPhotoLastSeenFlushListenerId;
        private long? _serverPhotoSyncPruneListenerId;

        internal ServerPhotoSeenService? PhotoSeenService => _serverPhotoSeenService;

        // True when this machine holds multiple copies of the current world sharing one photo
        // store (see PhotoStoreWorldMarker). /photoadmin's bulk time/count delete commands warn
        // (not block) on this, since selection could otherwise target another copy's live photos.
        internal bool PhotoStoreSharedByMultipleWorlds { get; private set; }

        private long _clientPhotoSeenLastPruneMs;
        private readonly Dictionary<string, long> _clientLastPhotoSeenPingMs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        internal void ServerTouchPhotoSeen(string photoId)
        {
            if (string.IsNullOrEmpty(photoId)) return;
            string normalized = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return;
            _serverPhotoSeenService?.Touch(normalized);
        }

        // Paired here, rather than left to callers, so gameplay that destroys a photo for good (a plate
        // reclaimed back to glass) cannot do half of it and orphan an index row against a deleted file.
        // Same two steps /photoadmin's delete runs per id.
        internal void ServerDeletePhoto(string photoId)
        {
            if (string.IsNullOrEmpty(photoId)) return;
            string normalized = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return;

            PhotoAssetStoragePaths.DeletePhotoAndDerived(normalized);
            _serverPhotoSeenService?.RemoveEntry(normalized);
        }

        internal void ClientMaybeSendPhotoSeen(string photoId)
        {
            if (_owner.ClientApi == null || _owner.ClientChannel == null) return;

            int intervalSeconds = _owner.Config?.PhotoSync?.PhotoSeenPingIntervalSeconds ?? 0;
            if (intervalSeconds <= 0) return;

            photoId = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoId)) return;

            if (_owner.ClientApi.World == null) return;
            long nowMs = _owner.ClientApi.World.ElapsedMilliseconds;

            // Keep the dedupe map bounded during long sessions.
            if (nowMs - _clientPhotoSeenLastPruneMs >= 30_000)
            {
                _clientPhotoSeenLastPruneMs = nowMs;

                long retainMs = Math.Max(300_000L, intervalSeconds * 4000L);
                List<string>? staleKeys = null;
                foreach (KeyValuePair<string, long> kvp in _clientLastPhotoSeenPingMs)
                {
                    if (nowMs - kvp.Value <= retainMs) continue;
                    staleKeys ??= new List<string>();
                    staleKeys.Add(kvp.Key);
                }

                if (staleKeys != null)
                {
                    foreach (string key in staleKeys)
                    {
                        _clientLastPhotoSeenPingMs.Remove(key);
                    }
                }
            }

            if (_clientLastPhotoSeenPingMs.TryGetValue(photoId, out long lastMs))
            {
                if (nowMs - lastMs < intervalSeconds * 1000L) return;
            }

            _clientLastPhotoSeenPingMs[photoId] = nowMs;
            _owner.ClientChannel.SendPacket(new PhotoSeenPacket { PhotoId = photoId });
        }
    }
}
