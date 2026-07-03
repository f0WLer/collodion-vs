using Vintagestory.API.Client;
using Photocore.PhotoSync.Store;

namespace Photocore.PhotoSync.Runtime
{
    // Client-side async waiters backing IPhotoStore.TryGetPhotoAsync. Separate from
    // _clientBlocksWaitingForPhoto (PhotoAssetSync.Client.cs): that mechanism re-tessellates specific
    // world blocks on arrival, this one resolves arbitrary callers (e.g. a third-party mod) with a result.
    public sealed partial class PhotoAssetSyncCore
    {
        // Plain dictionary: every access happens under the lock (the value lists need it anyway).
        private readonly Dictionary<string, List<TaskCompletionSource<PhotoFetchResult>>> _clientPhotoWaiters =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _clientPhotoWaitersLock = new();

        public async Task<PhotoFetchResult> ClientTryGetPhotoAsync(string photoId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return PhotoFetchResult.Missing;

            string path = PhotoAssetStoragePaths.GetPhotoPath(normalizedPhotoId);
            if (File.Exists(path))
            {
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
                    // Fall through to the wait path — a transient read failure shouldn't be reported
                    // as ConfirmedMissing when the file may simply be mid-write.
                }
            }

            if (ClientIsConfirmedMissing(normalizedPhotoId)) return PhotoFetchResult.Missing;

            // No client API means this runtime can never receive photo bytes (wrong side, or the
            // client is tearing down) — report missing rather than parking a waiter that cannot resolve.
            ICoreClientAPI? capi = _mod.ClientApi;
            if (capi == null) return PhotoFetchResult.Missing;

            var tcs = new TaskCompletionSource<PhotoFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_clientPhotoWaitersLock)
            {
                if (!_clientPhotoWaiters.TryGetValue(normalizedPhotoId, out List<TaskCompletionSource<PhotoFetchResult>>? waiters))
                {
                    waiters = new List<TaskCompletionSource<PhotoFetchResult>>();
                    _clientPhotoWaiters[normalizedPhotoId] = waiters;
                }
                waiters.Add(tcs);
            }

            using CancellationTokenRegistration reg = ct.Register(() =>
            {
                RemovePhotoWaiter(normalizedPhotoId, tcs);
                tcs.TrySetCanceled(ct);
            });

            // Request dispatch is marshalled to the main thread: ClientRequestPhotoIfMissing touches
            // main-thread-only state (_clientRequestedAt) and this public API may be called from any
            // thread. Packet handlers also run on the main thread, so re-checking the disk there closes
            // the race where the bytes landed between the check above and the waiter registration
            // (the request dedupe would otherwise swallow the re-request and strand the waiter).
            capi.Event.EnqueueMainThreadTask(() =>
            {
                if (File.Exists(path))
                {
                    try
                    {
                        ClientResolvePhotoWaiters(normalizedPhotoId, PhotoFetchResult.Found(File.ReadAllBytes(path)));
                        return;
                    }
                    catch { /* fall through to a re-request; the next arrival resolves the waiter */ }
                }
                ClientRequestPhotoIfMissing(normalizedPhotoId);
            }, "photocore-photostore-request");

            return await tcs.Task;
        }

        private void RemovePhotoWaiter(string photoId, TaskCompletionSource<PhotoFetchResult> tcs)
        {
            lock (_clientPhotoWaitersLock)
            {
                if (!_clientPhotoWaiters.TryGetValue(photoId, out List<TaskCompletionSource<PhotoFetchResult>>? waiters)) return;
                waiters.Remove(tcs);
                if (waiters.Count == 0) _clientPhotoWaiters.Remove(photoId);
            }
        }

        // Called from ClientHandleChunk (Found) and ClientHandleAck (Missing) once a photoId's fate is known.
        private void ClientResolvePhotoWaiters(string photoId, PhotoFetchResult result)
        {
            List<TaskCompletionSource<PhotoFetchResult>>? waiters;
            lock (_clientPhotoWaitersLock)
            {
                if (!_clientPhotoWaiters.Remove(photoId, out waiters) || waiters == null) return;
            }

            foreach (TaskCompletionSource<PhotoFetchResult> tcs in waiters)
            {
                tcs.TrySetResult(result);
            }
        }

        // Cancels every outstanding waiter so third-party awaits don't hang across a world unload.
        internal void ClientAbandonAllPhotoWaiters()
        {
            List<TaskCompletionSource<PhotoFetchResult>>? all = null;
            lock (_clientPhotoWaitersLock)
            {
                if (_clientPhotoWaiters.Count == 0) return;
                all = new List<TaskCompletionSource<PhotoFetchResult>>();
                foreach (List<TaskCompletionSource<PhotoFetchResult>> waiters in _clientPhotoWaiters.Values)
                {
                    all.AddRange(waiters);
                }
                _clientPhotoWaiters.Clear();
            }

            foreach (TaskCompletionSource<PhotoFetchResult> tcs in all)
            {
                tcs.TrySetCanceled();
            }
        }
    }
}
