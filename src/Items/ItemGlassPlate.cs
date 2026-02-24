using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    /// <summary>
    /// Places a dedicated glass plate block on the ground (one plate per block), instead of using GroundStorable/groundstorage.
    /// </summary>
    public sealed class ItemGlassPlate : ItemPlateBase
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (blockSel == null || api.World == null) return;

            // Plates are meant to be placed on the ground.
            if (blockSel.Face != BlockFacing.UP) return;

            handling = EnumHandHandling.PreventDefault;

            if (api.Side != EnumAppSide.Server)
            {
                return;
            }

            ItemStack? stack = slot.Itemstack;
            if (stack == null) return;

            string state = Attributes?["plateBlockState"].AsString("rough") ?? "rough";
            state = state == "clean" ? "clean" : "rough";

            BlockPos placePos = blockSel.Position.UpCopy();
            var world = api.World;

            Block existing = world.BlockAccessor.GetBlock(placePos);
            if (existing.Id != 0 && !existing.IsReplacableBy(existing))
            {
                return;
            }

            Block? plateBlock = world.GetBlock(new AssetLocation("collodion", $"plate-{state}"));
            if (plateBlock == null) return;

            world.BlockAccessor.SetBlock(plateBlock.Id, placePos);
            world.BlockAccessor.MarkBlockDirty(placePos);

            slot.TakeOut(1);
            slot.MarkDirty();
        }
    }
}
