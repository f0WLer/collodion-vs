using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Photocore.Plates
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

            // Block code template for the placed substrate, JSON-driven so paper (kosphotography:paper-*)
            // reuses this item class. Defaults to the glass plate blocks.
            string placedPrefix = Attributes?["placedBlockPrefix"]?.AsString("photocore:plate") ?? "photocore:plate";
            Block? plateBlock = world.GetBlock(new AssetLocation($"{placedPrefix}-{plateBlockState}"));
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

            // The reclaim count is restored outside the chemistry check: a reclaimed plate is rough and
            // has no chemistry, so gating on it would drop the count from exactly the plates that have one.
            if (world.BlockAccessor.GetBlockEntity(placePos) is BlockEntityGlassPlate be)
            {
                string? plateChemistry = slot.Itemstack.Attributes.GetString("plateChemistry", null);
                if (plateChemistry != null)
                {
                    be.ChemistryId = plateChemistry;
                    be.StepIndex = slot.Itemstack.Attributes.GetInt("plateStep", 1);
                }

                be.ReclaimCount = PlateAttributes.GetReclaimCount(slot.Itemstack);
                be.MarkDirty(true);
            }

            slot.TakeOut(1);
            slot.MarkDirty();
            ServerDebugLog.Notify(api, "plate-interact: place state={0} held={1}x{2} at {3} → placed", plateBlockState, heldCode, heldSize, placePos);
        }
    }
}