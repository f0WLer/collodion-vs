using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Photocore.PhotoSync.Runtime;
using Photocore.Configuration;

namespace Photocore.PhotoSync.Integration
{
    // Feature seam: centralizes client-side photo sync access so non-feature code avoids direct PhotoSync reach-through.
    internal static class ClientPhotoSyncIntegration
    {
        private static PhotoAssetSyncCore? ResolveClientPhotoSync(ICoreClientAPI capi)
        {
            return PhotocoreConfigAccess.ResolveClientModSystem(capi)?.PhotoSyncModSystemBridge.Runtime;
        }

        internal static void MaybeSendPhotoSeen(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return;
            PhotocoreConfigAccess.ResolveClientModSystem(capi)?.PhotoSyncModSystemBridge.ClientMaybeSendPhotoSeen(photoId);
        }

        internal static void NotifyPhotoCreated(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return;
            ResolveClientPhotoSync(capi)?.ClientOnPhotoCreated(photoId);
        }

        internal static void RequestPhotoIfMissing(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return;
            ResolveClientPhotoSync(capi)?.ClientRequestPhotoIfMissing(photoId);
        }

        // True once the server has confirmed (via a download NACK) that it cannot serve this photo, so the
        // render funnels should draw the missing-photo placeholder rather than keep waiting for a sync.
        internal static bool IsPhotoConfirmedMissing(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return false;
            return ResolveClientPhotoSync(capi)?.ClientIsConfirmedMissing(photoId) ?? false;
        }

        internal static void NoteBlockWaitingForPhoto(ICoreClientAPI capi, string photoId, BlockPos pos)
        {
            if (capi == null || string.IsNullOrEmpty(photoId) || pos == null) return;
            ResolveClientPhotoSync(capi)?.ClientNoteBlockWaitingForPhoto(photoId, pos);
        }
    }

    // Counterpart to NoteBlockWaitingForPhoto above. MarkBlockDirty alone only helps a block entity that
    // re-derives its mesh from scratch on every tesselation; one that caches a mesh behind its own
    // change-signature check would keep re-adding the stale mesh forever, since the photo arriving
    // doesn't change that signature.
    public interface IPhotoWaitingBlockEntity
    {
        void OnPhotoDelivered();
    }
}
