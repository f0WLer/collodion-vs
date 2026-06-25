using Photochemistry.AdminTooling;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Photochemistry.Configuration;

namespace Photochemistry.FieldCamera
{
    public sealed class BlockRestingCamera : Block
    {
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            return Array.Empty<ItemStack>();
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null || blockSel == null) return false;
            if (world.Side == EnumAppSide.Client) return true;

            PhotochemistryModSystem? modSys = PhotochemistryConfigAccess.ResolveModSystem(world.Api);
            if (modSys == null) return false;

            return modSys.FieldCameraBridge.TryHandleRestingCameraPickup(world, blockSel.Position, byPlayer);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world.Side == EnumAppSide.Server)
            {
                PhotochemistryModSystem? modSys = PhotochemistryConfigAccess.ResolveModSystem(world.Api);
                modSys?.FieldCameraBridge.HandleRestingCameraBlockBroken(world, pos);
            }

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }
}
