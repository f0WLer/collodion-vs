using Photochemistry.PhotoMetadata;
using Photochemistry.PhotoSync.Contracts;
using Photochemistry.PhotoSync.Storage;


namespace Photochemistry.PhotoSync.Integration
{
    // PhotoSync client seen-ping dedupe state and seen-touch helper.
    internal sealed partial class PhotoSyncModSystemBridge
    {
        private ServerPhotoSeenService? _serverPhotoSeenService;
        private long? _serverPhotoLastSeenFlushListenerId;
        private long? _serverPhotoSyncPruneListenerId;

        // Server-side last-seen index service, for operator disk-audit tooling. Null until server runtime
        // startup (ConfigureServerPhotoSyncRuntime) has run.
        internal ServerPhotoSeenService? PhotoSeenService => _serverPhotoSeenService;

        private long _clientPhotoSeenLastPruneMs;
        private readonly Dictionary<string, long> _clientLastPhotoSeenPingMs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        internal void ServerTouchPhotoSeen(string photoId)
        {
            if (string.IsNullOrEmpty(photoId)) return;
            string normalized = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return;
            _serverPhotoSeenService?.Touch(normalized);
        }

        // Sends deduplicated photo-seen pings so the server can track when a synced image has actually been observed by this client.
        internal void ClientMaybeSendPhotoSeen(string photoId)
        {
            if (_owner.ClientApi == null || _owner.ClientChannel == null) return;

            int intervalSeconds = _owner.ClientConfig?.PhotoSeenPingIntervalSeconds ?? 0;
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