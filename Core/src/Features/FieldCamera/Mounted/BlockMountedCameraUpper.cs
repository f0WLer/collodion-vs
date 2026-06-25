using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Photochemistry.Configuration;

namespace Photochemistry.FieldCamera
{
    // Invisible companion block entity for BlockMountedCameraUpper — suppresses default tesselation
    // so the upper block renders nothing on its own (visuals are owned by the lower block's renderer).
    public sealed class BlockEntityMountedCameraUpper : BlockEntity
    {
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
            => true;
    }


    // Invisible companion block placed one block above BlockMountedCamera when the camera
    // body extends past y=1.0. Delegates all interaction to the camera block below so the
    // player can right-click anywhere on the visible camera, not just the lower block.
    public sealed class BlockMountedCameraUpper : Block
    {
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            float ox = 0f, oz = 0f, top = 0f;
            if (blockAccessor.GetBlockEntity(pos.DownCopy()) is BlockEntityMountedCamera be)
            {
                ox  = be.SubBlockOffsetX;
                oz  = be.SubBlockOffsetZ;
                top = Math.Max(0f, be.SelectionTopY - 1f);
            }
            if (top <= 0f) return Array.Empty<Cuboidf>();
            return [new Cuboidf(0.3f + ox, 0f, 0.3f + oz, 0.7f + ox, top, 0.7f + oz)];
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
            => Array.Empty<Cuboidf>();

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
            => Array.Empty<ItemStack>();

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null || blockSel == null) return false;
            if (world.Side == EnumAppSide.Client) return true;

            PhotochemistryModSystem? modSys = PhotochemistryConfigAccess.ResolveModSystem(world.Api);
            if (modSys == null) return false;

            BlockPos camPos = blockSel.Position.DownCopy();
            bool shiftDown = byPlayer.Entity?.Controls?.ShiftKey == true || byPlayer.Entity?.Controls?.Sneak == true;
            bool ctrlDown  = byPlayer.Entity?.Controls?.CtrlKey == true;
            return modSys.FieldCameraBridge.TryHandleMountedCameraBlockInteract(world, camPos, byPlayer, shiftDown, ctrlDown);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            // Breaking the upper companion block also breaks the camera block below.
            // SetBlock(0) in BlockMountedCamera.OnBlockBroken will no-op since this block
            // is already being removed by VS (SetBlock does not fire OnBlockBroken).
            if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlock(pos.DownCopy()) is BlockMountedCamera)
                world.BlockAccessor.BreakBlock(pos.DownCopy(), byPlayer);

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }
}
