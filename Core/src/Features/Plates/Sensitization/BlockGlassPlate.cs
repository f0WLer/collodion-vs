using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Photochemistry.Plates
{
    public sealed partial class BlockGlassPlate : Block
    {
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Glass plates use per-texture alpha. If rendered in the opaque pass they write depth and can
            // cause "see under terrain" artifacts because terrain behind them never renders. Paper substrates
            // are fully opaque, so they stay in the opaque pass and read as a solid sheet instead of
            // translucent glass.
            bool isPaper = string.Equals(
                Attributes?["plateSubstrate"]?.AsString(null), "paper", System.StringComparison.OrdinalIgnoreCase);
            if (!isPaper)
                RenderPass = EnumChunkRenderPass.Transparent;
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            if (TryCreatePlateItemStack(world, pos, out ItemStack stack)) return [stack];

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;
            return HandleInteractionStart(world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;

            string state = GetPlateState();
            if (state == "rough")
            {
                return HandlePolishInteractionStep(secondsUsed, world, byPlayer, blockSel.Position);
            }

            bool isGroundSensitize = state == "clean" || state == "coated";
            if (!isGroundSensitize) return false;
            return HandleSensitizeStep(secondsUsed, world, byPlayer, blockSel.Position);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            string state = GetPlateState();
            if (state == "rough")
            {
                return BuildPolishInteractionHelp(world, selection, forPlayer);
            }

            if ((state == "clean" || state == "coated") && TryBuildSensitizationHint(world, selection?.Position, forPlayer, out WorldInteraction interaction))
            {
                return [interaction];
            }

            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if (TryCreatePlateItemStack(world, pos, out ItemStack stack)) return stack;

            return base.OnPickBlock(world, pos);
        }
    }
}


