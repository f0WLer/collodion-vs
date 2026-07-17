using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Photocore.PlateBox
{
    public sealed partial class BlockPlateBox
    {
        private static readonly AssetLocation _padlockSound = new("game", "sounds/tool/padlock");
        private const int OpenCloseSoundDelayMs = 80;

        private bool TryPickupBoxAndGiveDrop(IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (world == null || byPlayer == null || pos == null) return false;

            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null)
            {
                TryGiveOrSpawnStack(world, byPlayer, pos, stack);
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.RemoveBlockEntity(pos);
            return true;
        }

        private static bool TryGiveOrSpawnStack(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, ItemStack stack)
        {
            if (world == null || byPlayer == null || pos == null || stack == null) return false;

            bool given = byPlayer.InventoryManager?.TryGiveItemstack(stack) ?? false;
            if (given) return true;

            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            return true;
        }

        private static bool TrySetOpenState(IWorldAccessor world, BlockPos pos, BlockEntityPlateBox be, bool open)
        {
            if (world == null || pos == null || be == null) return false;

            string facing = world.BlockAccessor.GetBlock(pos)?.Variant?["facing"] ?? "south";
            AssetLocation targetCode = open
                ? new AssetLocation("photocore", "platebox-open-" + facing)
                : new AssetLocation("photocore", "platebox-" + facing);
            Block? target = world.GetBlock(targetCode);
            if (target == null)
            {
                return be.SetOpen(open) || be.IsOpen == open;
            }

            if (world.BlockAccessor.GetBlock(pos)?.Code == targetCode)
            {
                bool changedSameBlock = be.SetOpen(open) || be.IsOpen == open;
                if (changedSameBlock)
                {
                    PlayOpenCloseSoundPair(world, pos);
                }

                return changedSameBlock;
            }

            var snapshot = new TreeAttribute();
            be.SetOpen(open);
            be.ToTreeAttributes(snapshot);

            world.BlockAccessor.SetBlock(target.Id, pos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPlateBox newBe)
            {
                newBe.FromTreeAttributes(snapshot, world);
                newBe.MarkDirty(true);
                world.BlockAccessor.MarkBlockEntityDirty(pos);
                world.BlockAccessor.MarkBlockDirty(pos);
                PlayOpenCloseSoundPair(world, pos);
                return true;
            }

            return false;
        }

        private static void PlayOpenCloseSoundPair(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null || world.Side != EnumAppSide.Server) return;

            double x = pos.X + 0.5;
            double y = pos.Y + 0.5;
            double z = pos.Z + 0.5;

            PlaySoundWithDelay(world, x, y, z, _padlockSound, 0);
            PlaySoundWithDelay(world, x, y, z, _padlockSound, OpenCloseSoundDelayMs);
        }

        // pitch null keeps the original randomize-pitch behavior; callers that already rolled their
        // own pitch (e.g. per-plate thuds) pass it through explicitly instead of rolling a second one.
        private static void PlaySoundWithDelay(IWorldAccessor world, double x, double y, double z, AssetLocation sound, int delayMs, float? pitch = null, float volume = 1f)
        {
            if (delayMs <= 0)
            {
                PlaySoundNow(world, x, y, z, sound, pitch, volume);
                return;
            }

            try
            {
                world.Api?.Event?.RegisterCallback(_ =>
                {
                    try
                    {
                        if (world.Side != EnumAppSide.Server) return;
                        PlaySoundNow(world, x, y, z, sound, pitch, volume);
                    }
                    catch (Exception ex) { Log.Debug(world.Logger, "PlaySoundWithDelay callback failed: {0}", ex.Message); }
                }, delayMs);
            }
            catch (Exception ex)
            {
                Log.Warn(world.Logger, "PlaySoundWithDelay scheduling failed, using immediate fallback: {0}", ex.Message);
                PlaySoundNow(world, x, y, z, sound, pitch, volume);
            }
        }

        private static void PlaySoundNow(IWorldAccessor world, double x, double y, double z, AssetLocation sound, float? pitch, float volume)
        {
            if (pitch.HasValue) world.PlaySoundAt(sound, x, y, z, null, pitch.Value, 16f, volume);
            else world.PlaySoundAt(sound, x, y, z, null, true, 16f, volume);
        }
    }
}
