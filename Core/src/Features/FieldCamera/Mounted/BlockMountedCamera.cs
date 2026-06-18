using Collodion.AdminTooling;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion.FieldCamera
{
    public sealed class BlockMountedCamera : Block
    {
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            float ox = 0f, oz = 0f;
            if (blockAccessor.GetBlockEntity(pos) is BlockEntityMountedCamera be)
            {
                ox = be.SubBlockOffsetX;
                oz = be.SubBlockOffsetZ;
            }
            return [new Cuboidf(0.3f + ox, 0f, 0.3f + oz, 0.7f + ox, 1f, 0.7f + oz)];
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            return Array.Empty<ItemStack>();
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null || blockSel == null) return false;
            if (world.Side == EnumAppSide.Client) return true;

            CollodionModSystem? modSys = CollodionConfigAccess.ResolveModSystem(world.Api);
            if (modSys == null) return false;

            bool shiftDown = byPlayer?.Entity?.Controls?.ShiftKey == true || byPlayer?.Entity?.Controls?.Sneak == true;
            bool ctrlDown  = byPlayer?.Entity?.Controls?.CtrlKey == true;
            return modSys.FieldCameraBridge.TryHandleMountedCameraBlockInteract(world, blockSel.Position, byPlayer, shiftDown, ctrlDown);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world.Side == EnumAppSide.Server)
            {
                CollodionModSystem? modSys = CollodionConfigAccess.ResolveModSystem(world.Api);
                modSys?.FieldCameraBridge.HandleMountedCameraBlockBroken(world, pos, byPlayer);

                // Clear the invisible companion block above so it isn't orphaned in the air.
                // SetBlock(0) (not BreakBlock) avoids re-entering BlockMountedCameraUpper.OnBlockBroken,
                // which would call back into this block while it is already being removed.
                BlockPos abovePos = pos.UpCopy();
                if (world.BlockAccessor.GetBlock(abovePos) is BlockMountedCameraUpper)
                    world.BlockAccessor.SetBlock(0, abovePos);
            }

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }
}