using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public partial class ItemWetplateCamera : Item
    {
        public const string AttrLoadedPlate = "collodionLoadedPlate";
        public const string AttrLoadedPlateStack = "collodionLoadedPlateStack";

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;

            // Viewfinder mode is driven by the client tick polling RMB state in CollodionModSystem.
            // We still prevent default use/interact while holding the camera.
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine("RMB + LMB to look through the viewfinder and expose a plate.");

            string? loadedPlate = inSlot?.Itemstack?.Attributes?.GetString(AttrLoadedPlate, null);
            if (!string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine($"Loaded plate: {loadedPlate}");
            }
            else
            {
                dsc.AppendLine("Loaded plate: (none)");
            }

            if (string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine("Shift+Right click with a Silvered Plate in offhand to load.");
            }
            else
            {
                dsc.AppendLine("Shift+Right click with empty offhand to unload.");
            }
        }
    }
}
