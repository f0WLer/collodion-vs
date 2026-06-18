using Collodion.AdminTooling;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion.Plates
{
    public sealed partial class ItemGlassPlate : ItemPlateBase
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (api.World == null || slot?.Itemstack == null) return;
            if (blockSel == null) return;
            if (blockSel.Face != BlockFacing.UP) return;

            handling = EnumHandHandling.PreventDefault;

            if (api.Side != EnumAppSide.Server) return;

            string defaultPlateBlockState = Attributes?["plateBlockState"].AsString("rough") ?? "rough";
            string plateBlockState = slot.Itemstack.Attributes.GetString("plateBlockState", defaultPlateBlockState);
            plateBlockState = plateBlockState switch
            {
                "clean" => "clean",
                "coated" => "coated",
                _ => "rough"
            };

            BlockPos placePos = blockSel.Position.UpCopy();
            IWorldAccessor world = api.World;

            string heldCode = slot.Itemstack.Collectible?.Code?.ToString() ?? "null";
            int heldSize = slot.Itemstack.StackSize;

            Block? plateBlock = world.GetBlock(new AssetLocation("collodion", $"plate-{plateBlockState}"));
            if (plateBlock == null)
            {
                ServerDebugLog.Notify(api, "plate-interact: place state={0} held={1}x{2} → declined: plate-{0} block not found", plateBlockState, heldCode, heldSize);
                return;
            }

            Block existing = world.BlockAccessor.GetBlock(placePos);
            if (existing.Id != 0 && !existing.IsReplacableBy(plateBlock))
            {
                ServerDebugLog.Notify(api, "plate-interact: place state={0} held={1}x{2} at {3} → declined: occupied by {4}", plateBlockState, heldCode, heldSize, placePos, existing.Code);
                return;
            }

            world.BlockAccessor.SetBlock(plateBlock.Id, placePos);
            slot.TakeOut(1);
            slot.MarkDirty();
            ServerDebugLog.Notify(api, "plate-interact: place state={0} held={1}x{2} at {3} → placed", plateBlockState, heldCode, heldSize, placePos);
        }
    }
}