namespace Photocore.PhotoSync.Store
{
    public enum PhotoFetchOutcome
    {
        // Bytes are attached and valid.
        Found,

        // Authoritative: no such photo exists (bad id, or the server confirmed it is gone).
        // A cancelled/timed-out wait is NOT this outcome — it surfaces as a cancelled Task instead,
        // since "gave up waiting" and "definitively does not exist" are different facts for a caller to act on.
        ConfirmedMissing
    }

    public readonly struct PhotoFetchResult
    {
        public PhotoFetchOutcome Outcome { get; }
        public byte[]? Bytes { get; }

        private PhotoFetchResult(PhotoFetchOutcome outcome, byte[]? bytes)
        {
            Outcome = outcome;
            Bytes = bytes;
        }

        public static PhotoFetchResult Found(byte[] bytes) => new(PhotoFetchOutcome.Found, bytes);

        public static readonly PhotoFetchResult Missing = new(PhotoFetchOutcome.ConfirmedMissing, null);
    }

    // Read-only public surface for other mods to access a Photocore/Collodion source photo's image
    // bytes. Resolve via PhotocoreModSystem.PhotoStore (found through
    // capi.ModLoader.GetModSystem<PhotocoreModSystem>(withInheritance: true) so callers work
    // regardless of which head — collodion or kosphotography — is actually installed).
    //
    // Save/Delete are deliberately not part of this surface: writing into Photocore's photo store is
    // a materially bigger trust boundary than reading from it, and nothing needs it yet.
    public interface IPhotoStore
    {
        // Server: resolves near-instantly from local disk (the server is always authoritative).
        // Client: the photo may not be locally cached yet, so this can enqueue a throttled download
        // and wait for it. The task completes with Found once bytes arrive, ConfirmedMissing once the
        // server NACKs the id, or cancels if the token fires first. Client-side delivery is
        // best-effort (packets can be lost with no retry on this path), so callers that cannot wait
        // indefinitely should pass a token with a deadline rather than the default.
        //
        // The returned task's continuation resumes on a threadpool thread, not the main thread (there
        // is no game synchronization context to capture). Bounce any UI/world/render work — including
        // chat, texture upload, and mesh building — back onto the main thread yourself (e.g. via
        // capi.Event.EnqueueMainThreadTask); touching engine state from the continuation races the
        // render thread and crashes.
        Task<PhotoFetchResult> TryGetPhotoAsync(string photoId, CancellationToken ct = default);

        // Cheap local-cache probe; never triggers a fetch. On the client, false means "not synced
        // (yet)", not "doesn't exist" — use TryGetPhotoAsync for a definitive answer.
        bool ExistsLocally(string photoId);

        // The type tag is the substring of the id before its first '_' (e.g. "exposure"). Returns
        // empty for a malformed id rather than throwing.
        string GetTypeTag(string photoId);

        // Enumerates known ids, optionally filtered to one type tag. Server: the full on-disk store.
        // Client: whatever is in the local cache (not the server's full set).
        IReadOnlyList<string> EnumerateIds(string? typeTag = null);
    }
}
