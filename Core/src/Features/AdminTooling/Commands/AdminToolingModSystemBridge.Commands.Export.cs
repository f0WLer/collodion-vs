using Photochemistry.PhotoMetadata.Model;
using Photochemistry.Plates;
using Photochemistry.Plates.Rendering;
using Photochemistry.PhotoSync.Storage;
using Vintagestory.API.Common;

namespace Photochemistry.AdminTooling
{
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
                capi.ShowChatMessage("photochemistry: hold a developed photo plate to export.");
                return;
            }

            PlateStage stage = PlateAttributes.GetStage(held);
            if (stage != PlateStage.Developed && stage != PlateStage.Finished)
            {
                capi.ShowChatMessage("photochemistry: develop the plate fully before exporting.");
                return;
            }

            string sourcePath = PhotoAssetStoragePaths.GetPhotoPath(photoId);
            if (!File.Exists(sourcePath))
            {
                capi.ShowChatMessage("photochemistry: that photo isn't synced yet — try again in a moment.");
                return;
            }

            // Friendly export name: caption (if any) + the raw id (already timestamp-encoded).
            string caption = held.Attributes.GetString(PhotographAttrs.Caption) ?? string.Empty;
            string baseId = Path.GetFileNameWithoutExtension(photoId);
            string friendly = string.IsNullOrWhiteSpace(caption) ? baseId : $"{caption}_{baseId}";
            string outPath = PhotoAssetStoragePaths.GetExportPath(friendly);

            if (PhotoImageProcessor.TryWriteCompositePng(sourcePath, outPath, PlatePresentation.Resolve(held)))
                capi.ShowChatMessage($"photochemistry: exported photo to {outPath}");
            else
                capi.ShowChatMessage("photochemistry: failed to export photo (see client log).");
        }
    }
}
