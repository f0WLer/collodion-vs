using Vintagestory.API.Common;

namespace Collodion
{
    public class ItemCameraSling : Item
    {
        public const string AttrStoredCameraStack = "collodionStoredCameraStack";

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            dsc.AppendLine("Wear in left shoulder slot.");
            dsc.AppendLine("Press R to store/unstore camera from active slot.");

            ItemStack? stored = null;
            try
            {
                stored = inSlot?.Itemstack?.Attributes?.GetItemstack(AttrStoredCameraStack, null);
            }
            catch
            {
                stored = null;
            }

            dsc.AppendLine(stored == null ? "Stored camera: (none)" : "Stored camera: loaded");
        }
    }
}
