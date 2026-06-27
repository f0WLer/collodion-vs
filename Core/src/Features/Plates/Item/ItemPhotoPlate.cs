using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Photocore.Plates.Rendering;

namespace Photocore.Plates
{
    public sealed class ItemPhotoPlate : ItemPlateBase
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

            if (PlateAttributes.GetStage(slot.Itemstack) == PlateStage.Finished)
            {
                PlateDryingTransition.Clear(slot.Itemstack);
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack? stack = inSlot?.Itemstack;
            if (stack == null) return;

            if (PlateAttributes.GetStage(stack) != PlateStage.Finished)
            {
                PlateDryingTransition.AppendInfo(world, stack, dsc);
            }
        }

        protected override bool ShouldTrackDryness(ItemStack stack)
            => PlateAttributes.GetStage(stack) != PlateStage.Finished;
    }
}
