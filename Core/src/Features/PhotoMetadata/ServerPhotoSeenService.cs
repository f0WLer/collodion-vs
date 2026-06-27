using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Photocore.PhotoMetadata.Model;
using Photocore.PhotoSync.Storage;

namespace Photocore.PhotoMetadata
{
    // Owns the server-side photo-seen index: in-memory state, dirty tracking, and persistence.
    // TryFlush is single-flight (Interlocked guard) and dispatches the actual file write through
    // TyronThreadPool.QueueTask so it never blocks the main server thread or a packet handler.
    internal sealed class ServerPhotoSeenService
    {
        private readonly string _configFileName;
        private readonly PhotoLastSeenIndex _index;
        private bool _isDirty;

        // Guards against overlapping flush tasks. Only one persist-to-disk task may be in flight.
        private int _flushInFlight;

        private ServerPhotoSeenService(string configFileName, PhotoLastSeenIndex index)
        {
            _configFileName = configFileName;
            _index = index;
        }

        internal static ServerPhotoSeenService LoadOrCreate(ICoreServerAPI sapi, string configFileName)
        {
            PhotoLastSeenIndex? loaded = null;
            try
            {
                loaded = sapi.LoadModConfig<PhotoLastSeenIndex>(configFileName);
            }
            catch
            {
                loaded = null;
            }

            if (loaded == null)
            {
                loaded = new PhotoLastSeenIndex();
                try
                {
                    loaded.ClampInPlace();
                    sapi.StoreModConfig(loaded, configFileName);
                }
                catch
                {
                    // ignore
                }
            }

            loaded.ClampInPlace();
            return new ServerPhotoSeenService(configFileName, loaded);
        }

        internal void Touch(string photoId)
        {
            _index.Touch(photoId);
            _isDirty = true;
        }

        // Snapshots the current index rows (cloned) for read-only audit on the main thread. The clone
        // keeps callers isolated from concurrent Touch() writes between snapshot and use.
        internal IReadOnlyDictionary<string, PhotoLastSeenEntry> SnapshotEntries()
        {
            var copy = new Dictionary<string, PhotoLastSeenEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, PhotoLastSeenEntry> kvp in _index.Entries)
            {
                if (kvp.Value == null) continue;
                copy[kvp.Key] = new PhotoLastSeenEntry
                {
                    FirstSeenUtc = kvp.Value.FirstSeenUtc,
                    LastSeenUtc = kvp.Value.LastSeenUtc
                };
            }
            return copy;
        }

        internal bool RemoveEntry(string photoId)
        {
            string normalized = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return false;

            if (_index.Entries.Remove(normalized))
            {
                _isDirty = true;
                return true;
            }
            return false;
        }

        internal int RemoveEntriesWithoutFile(System.Func<string, bool> fileExists)
        {
            List<string>? stale = null;
            foreach (KeyValuePair<string, PhotoLastSeenEntry> kvp in _index.Entries)
            {
                if (fileExists(kvp.Key)) continue;
                stale ??= new List<string>();
                stale.Add(kvp.Key);
            }

            if (stale == null) return 0;

            foreach (string id in stale) _index.Entries.Remove(id);
            _isDirty = true;
            return stale.Count;
        }

        // Called on the main server thread by a periodic tick listener. Snapshots the index and dispatches
        // the actual JSON serialize + disk write to the thread pool so the tick is not blocked by I/O.
        internal void TryFlush(ICoreServerAPI sapi)
        {
            if (!_isDirty) return;

            if (Interlocked.CompareExchange(ref _flushInFlight, 1, 0) != 0) return;

            // Optimistically clear dirty; restore on failure so the next tick retries.
            _isDirty = false;

            // Snapshot under control of the main thread before handing off to the thread pool.
            // The clone keeps the thread-pool serializer isolated from concurrent main-thread Touch() writes.
            PhotoLastSeenIndex snapshot;
            try
            {
                _index.ClampInPlace();
                snapshot = new PhotoLastSeenIndex();
                foreach (KeyValuePair<string, PhotoLastSeenEntry> kvp in _index.Entries)
                {
                    if (kvp.Value == null) continue;
                    snapshot.Entries[kvp.Key] = new PhotoLastSeenEntry
                    {
                        FirstSeenUtc = kvp.Value.FirstSeenUtc,
                        LastSeenUtc = kvp.Value.LastSeenUtc
                    };
                }
            }
            catch
            {
                _isDirty = true;
                Interlocked.Exchange(ref _flushInFlight, 0);
                return;
            }

            string fileName = _configFileName;

            TyronThreadPool.QueueTask(() =>
            {
                try
                {
                    sapi.StoreModConfig(snapshot, fileName);
                }
                catch
                {
                    // Mark dirty so periodic flush retries. Main-thread visibility comes from next tick read.
                    _isDirty = true;
                }
                finally
                {
                    Interlocked.Exchange(ref _flushInFlight, 0);
                }
            }, "photocore:SeenIndexFlush");
        }
    }
}
