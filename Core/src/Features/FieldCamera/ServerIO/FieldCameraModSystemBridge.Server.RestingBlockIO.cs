using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Photochemistry.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        internal bool TryHandleRestingCameraPickup(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return false;
            if (byPlayer is not IServerPlayer serverPlayer) return false;
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityRestingCamera restingBe) return false;

            ItemStack? recovered = restingBe.TakeStoredCameraStack(Api.World);
            if (recovered == null) return false;

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.RemoveBlockEntity(pos);

            TryGiveOrSpawnMountedCamera(world, serverPlayer, pos, recovered);
            return true;
        }

        internal void HandleRestingCameraBlockBroken(IWorldAccessor world, BlockPos pos)
        {
            if (Api?.World == null) return;
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityRestingCamera restingBe) return;

            ItemStack? dropped = restingBe.TakeStoredCameraStack(Api.World);
            if (dropped != null)
                world.SpawnItemEntity(dropped, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}
