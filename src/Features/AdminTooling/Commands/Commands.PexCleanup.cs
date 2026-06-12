using Collodion.Exposure;
using Collodion.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion.AdminTooling
{
    internal sealed partial class AdminToolingModSystemBridge
    {
        // Handles .collodion clearpex [confirm].
        // Dry-run (no confirm): reports how many .pex files exist and how many are protected
        // by plates currently in the player's accessible inventories.
        // With confirm: deletes only the unprotected (orphaned) files.
        internal void HandleClearPexCommand(CmdArgs args)
        {
            var capi = _owner.ClientApi;
            if (capi == null) return;

            bool confirm = args.PopWord()?.Equals("confirm", StringComparison.OrdinalIgnoreCase) == true;

            IReadOnlyList<string> allIds = ExposureAccumulationStore.EnumerateIds();
            if (allIds.Count == 0)
            {
                capi.ShowChatMessage("Collodion: no partial exposure files found.");
                return;
            }

            HashSet<string> protectedIds = CollectInventoryExposureIds(capi);
            var toDelete = new List<string>(allIds.Count);
            foreach (string id in allIds)
            {
                if (!protectedIds.Contains(id)) toDelete.Add(id);
            }
            int protectedCount = allIds.Count - toDelete.Count;

            if (!confirm)
            {
                if (toDelete.Count == 0)
                {
                    capi.ShowChatMessage($"Collodion: {allIds.Count} partial exposure file(s) found, all protected by plates in your inventory.");
                    return;
                }
                capi.ShowChatMessage(
                    $"Collodion: {allIds.Count} partial exposure file(s) — {protectedCount} protected by plates in your inventory, {toDelete.Count} orphaned. "
                    + "Run '.collodion clearpex confirm' to delete orphaned files.");
                return;
            }

            if (toDelete.Count == 0)
            {
                capi.ShowChatMessage("Collodion: nothing to delete — all partial exposure files are protected by plates in your inventory.");
                return;
            }

            foreach (string id in toDelete)
                ExposureAccumulationStore.Delete(id);

            capi.ShowChatMessage($"Collodion: deleted {toDelete.Count} orphaned partial exposure file(s). {protectedCount} kept.");
        }

        // Walks all inventories currently accessible to the player and collects every
        // exposure ID referenced by a plate or fieldcamera-loaded plate, to protect those
        // .pex files from the cleanup command.
        private HashSet<string> CollectInventoryExposureIds(ICoreClientAPI capi)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (IInventory inv in capi.World.Player.InventoryManager.Inventories.Values)
            {
                foreach (ItemSlot slot in inv)
                {
                    if (slot.Empty) continue;
                    TryAddExposureId(slot.Itemstack, capi.World, ids);
                }
            }
            return ids;
        }

        private static void TryAddExposureId(ItemStack stack, IWorldAccessor world, HashSet<string> ids)
        {
            string? id = stack.Attributes?.GetString(PlateAttributes.ExposureId);
            if (!string.IsNullOrEmpty(id)) { ids.Add(id); return; }

            if (CameraItemHelper.TryGetLoadedPlateStack(stack, world, out ItemStack? plate) && plate != null)
            {
                id = plate.Attributes?.GetString(PlateAttributes.ExposureId);
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
        }
    }
}
