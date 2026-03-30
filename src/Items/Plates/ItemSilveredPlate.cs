using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public class ItemSilveredPlate : ItemPlateBase
    {
        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, GridRecipe recipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, recipe);

            if (api?.World == null) return;
            ItemStack? outStack = outputSlot?.Itemstack;
            if (outStack == null) return;

            double duration = WetPlateAttrs.ResolveWetDurationHours(api);
            WetPlateAttrs.EnsureWetTimer(api.World, outStack, duration);
        }

        public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack? extractedStack = null)
        {
            base.OnModifiedInInventorySlot(world, slot, extractedStack);
            if (world?.Side != EnumAppSide.Server) return;
            if (slot?.Itemstack == null) return;
            double duration = WetPlateAttrs.ResolveWetDurationHours(api);
            WetPlateAttrs.EnsureWetTimer(world, slot.Itemstack, duration);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack? stack = inSlot?.Itemstack;
            if (stack == null) return;

            WetPlateAttrs.AppendWetnessInfo(world, stack, dsc);
        }
    }
}
