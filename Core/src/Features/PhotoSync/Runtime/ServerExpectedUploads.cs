namespace Photochemistry.PhotoSync.Runtime
{
    internal sealed class ServerExpectedUploads
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _openByPlayer = new(StringComparer.Ordinal);

        private long _lastPruneMs;
        private readonly long _ttlMs;
        private const long PruneIntervalMs = 30_000;

        public ServerExpectedUploads(long ttlMs)
        {
            _ttlMs = Math.Max(5_000L, ttlMs);
        }

        public void Register(string playerUid, string photoId, long nowMs)
        {
            if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(photoId)) return;

            string key = MakeKey(playerUid, photoId);
            lock (_lock)
            {
                MaybePrune_NoLock(nowMs);
                _entries[key] = new Entry { ExpiresAtMs = nowMs + _ttlMs };
            }
        }

        public bool IsExpected(string playerUid, string photoId, long nowMs)
        {
            if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(photoId)) return false;

            string key = MakeKey(playerUid, photoId);
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out Entry e)) return false;
                if (nowMs > e.ExpiresAtMs)
                {
                    _entries.Remove(key);
                    return false;
                }
                return true;
            }
        }

        public void Consume(string playerUid, string photoId)
        {
            if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(photoId)) return;
            string key = MakeKey(playerUid, photoId);
            lock (_lock) { _entries.Remove(key); }
        }

        public bool TryBeginUpload(string playerUid, int maxOpenPerPlayer)
        {
            if (string.IsNullOrEmpty(playerUid)) return false;
            if (maxOpenPerPlayer < 1) maxOpenPerPlayer = 1;

            lock (_lock)
            {
                _openByPlayer.TryGetValue(playerUid, out int count);
                if (count >= maxOpenPerPlayer) return false;
                _openByPlayer[playerUid] = count + 1;
                return true;
            }
        }

        public void EndUpload(string playerUid)
        {
            if (string.IsNullOrEmpty(playerUid)) return;
            lock (_lock)
            {
                if (!_openByPlayer.TryGetValue(playerUid, out int count)) return;
                count--;
                if (count <= 0) _openByPlayer.Remove(playerUid);
                else _openByPlayer[playerUid] = count;
            }
        }

        private void MaybePrune_NoLock(long nowMs)
        {
            if (_lastPruneMs != 0 && (nowMs - _lastPruneMs) < PruneIntervalMs) return;
            _lastPruneMs = nowMs;

            if (_entries.Count == 0) return;

            List<string>? stale = null;
            foreach (KeyValuePair<string, Entry> kvp in _entries)
            {
                if (nowMs > kvp.Value.ExpiresAtMs)
                {
                    stale ??= new List<string>();
                    stale.Add(kvp.Key);
                }
            }

            if (stale == null) return;
            foreach (string k in stale) _entries.Remove(k);
        }

        private static string MakeKey(string playerUid, string photoId) => playerUid + "|" + photoId;

        private struct Entry
        {
            public long ExpiresAtMs;
        }
    }
}
