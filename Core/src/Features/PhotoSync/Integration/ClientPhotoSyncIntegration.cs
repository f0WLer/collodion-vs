using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Photochemistry.PhotoSync.Runtime;
using Photochemistry.Configuration;

namespace Photochemistry.PhotoSync.Integration
{
    // Feature seam: centralizes client-side photo sync access so non-feature code avoids direct PhotoSync reach-through.
    internal static class ClientPhotoSyncIntegration
    {
        private static PhotoAssetSyncCore? ResolveClientPhotoSync(ICoreClientAPI capi)
        {
            return PhotochemistryConfigAccess.ResolveClientModSystem(capi)?.PhotoSyncModSystemBridge.Runtime;
        }

        internal static void MaybeSendPhotoSeen(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return;
            PhotochemistryConfigAccess.ResolveClientModSystem(capi)?.PhotoSyncModSystemBridge.ClientMaybeSendPhotoSeen(photoId);
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

        internal static void NoteBlockWaitingForPhoto(ICoreClientAPI capi, string photoId, BlockPos pos)
        {
            if (capi == null || string.IsNullOrEmpty(photoId) || pos == null) return;
            ResolveClientPhotoSync(capi)?.ClientNoteBlockWaitingForPhoto(photoId, pos);
        }
    }
}
