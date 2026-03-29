using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public class ItemDevelopedPlate : ItemPlateBase
    {
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            PhotoPlateRenderUtil.TryRenderPhotoOverlay(capi, itemstack, target, ref renderinfo);
        }

        public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack? extractedStack = null)
        {
            base.OnModifiedInInventorySlot(world, slot, extractedStack);
            if (world?.Side != EnumAppSide.Server) return;
            if (slot?.Itemstack == null) return;
            double duration = api?.ModLoader?.GetModSystem<CollodionModSystem>()?.Config?.PlateProcessing?.WetPlateDurationHours ?? WetPlateAttrs.DefaultWetDurationHours;
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
