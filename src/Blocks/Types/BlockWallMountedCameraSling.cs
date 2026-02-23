using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public sealed class BlockWallMountedCameraSling : Block
    {
        private static readonly AssetLocation SlingFullCode = new AssetLocation("collodion", "camerasling-full");

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityWallMountedCameraSling be)
            {
                ItemStack? stack = be.SlingStack?.Clone();
                if (stack != null)
                {
                    return new[] { stack };
                }
            }

            Item? fallback = world?.GetItem(SlingFullCode);
            if (fallback != null)
            {
                return new[] { new ItemStack(fallback) };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityWallMountedCameraSling be)
            {
                ItemStack? stack = be.SlingStack?.Clone();
                if (stack != null) return stack;
            }

            Item? fallback = world?.GetItem(SlingFullCode);
            if (fallback != null)
            {
                return new ItemStack(fallback);
            }

            return base.OnPickBlock(world, pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel?.Position == null) return false;
            if (world.Side != EnumAppSide.Server) return true;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityWallMountedCameraSling be)
            {
                return false;
            }

            ItemStack? stack = be.TakeSlingStack();
            if (stack == null)
            {
                Item? fallback = world.GetItem(SlingFullCode);
                if (fallback != null) stack = new ItemStack(fallback);
            }

            if (stack != null)
            {
                if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
                {
                    world.SpawnItemEntity(stack, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            world.BlockAccessor.SetBlock(0, blockSel.Position);
            world.BlockAccessor.RemoveBlockEntity(blockSel.Position);
            return true;
        }
    }
}
