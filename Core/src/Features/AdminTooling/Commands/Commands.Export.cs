using Collodion.PhotoMetadata.Model;
using Collodion.Plates;
using Collodion.Plates.Rendering;
using Collodion.PhotoSync.Storage;
using Vintagestory.API.Common;

namespace Collodion.AdminTooling
{
    // .collodion export — bakes the currently held photo plate into a flat, viewable composite
    // PNG (silver image over an opaque black backing) under ModData/collodion/photos/exports/.
    // Entirely client-side: reads the already-synced raw, writes a derived file, no server I/O.
    internal sealed partial class AdminToolingModSystemBridge
    {
        internal void HandleModExportCommand()
        {
            var capi = _owner.ClientApi;
            if (capi == null) return;

            ItemStack? held = capi.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            string photoId = held?.Attributes?.GetString(PhotographAttrs.PhotoId) ?? string.Empty;
            if (held == null || string.IsNullOrEmpty(photoId))
            {
                capi.ShowChatMessage("Collodion: hold a developed photo plate to export.");
                return;
            }

            PlateStage stage = PlateAttributes.GetStage(held);
            if (stage != PlateStage.Developed && stage != PlateStage.Finished)
            {
                capi.ShowChatMessage("Collodion: develop the plate fully before exporting.");
                return;
            }

            string sourcePath = PhotoAssetStoragePaths.GetPhotoPath(photoId);
            if (!File.Exists(sourcePath))
            {
                capi.ShowChatMessage("Collodion: that photo isn't synced yet — try again in a moment.");
                return;
            }

            // Friendly export name: caption (if any) + the raw id (already timestamp-encoded).
            string caption = held.Attributes.GetString(PhotographAttrs.Caption) ?? string.Empty;
            string baseId = Path.GetFileNameWithoutExtension(photoId);
            string friendly = string.IsNullOrWhiteSpace(caption) ? baseId : $"{caption}_{baseId}";
            string outPath = PhotoAssetStoragePaths.GetExportPath(friendly);

            if (PhotoImageProcessor.TryWriteCompositePng(sourcePath, outPath))
                capi.ShowChatMessage($"Collodion: exported photo to {outPath}");
            else
                capi.ShowChatMessage("Collodion: failed to export photo (see client log).");
        }
    }
}
