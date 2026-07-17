using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Photocore.PlateBox
{
    // Plate-box block identity, placement, drop serialization, and partial-family routing.
    // Starts from block lifecycle callbacks and delegates rendering/help and slot interaction to sibling partials.
    // Delegates persistent slot state to BlockEntityPlateBox and presentation/help to sibling partials.
    // Side: mixed block owner. Keep authoritative slot mutation in the interaction partial, not in render/presentation helpers.
    // Related files: BlockPlateBox.Interaction.cs, BlockPlateBox.WorldMutationSeam.cs, BlockEntityPlateBox.cs.
    public sealed partial class BlockPlateBox : Block
    {
        private static readonly AssetLocation _samplePlateCode = new("photocore", "sensitizedplate");
        private static readonly AssetLocation _closedBoxCode = new("photocore", "platebox-north");
        private static readonly Cuboidf[] _slotHitBoxes =
        [
            // Matches platehb1..platehb8 in assets/collodion/shapes/block/platebox-open.json. Slot
            // resolution uses only each box's X center (see GetNearestSlotIndex); the Y/Z extents are
            // unused there but kept since they document the plate geometry.
            new(1.5f / 16f, 0.5f / 16f, 4.5f / 16f, 2.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(3.0f / 16f, 0.5f / 16f, 4.5f / 16f, 3.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(4.5f / 16f, 0.5f / 16f, 4.5f / 16f, 5.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(6.0f / 16f, 0.5f / 16f, 4.5f / 16f, 6.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(9.5f / 16f, 0.5f / 16f, 4.5f / 16f, 10.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(11.0f / 16f, 0.5f / 16f, 4.5f / 16f, 11.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(12.5f / 16f, 0.5f / 16f, 4.5f / 16f, 13.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(14.0f / 16f, 0.5f / 16f, 4.5f / 16f, 14.5f / 16f, 8.2f / 16f, 11.5f / 16f)
        ];
        
        private static readonly AssetLocation _woodThudSound = new("photocore", "sounds/wood-thud");
        private static readonly AssetLocation[] _glassThudSounds =
        [
            new("photocore", "sounds/glass-thud1"),
            new("photocore", "sounds/glass-thud2")
        ];
        
        // Hides rotated variants in creative tabs so only the canonical north item appears.
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            PartialSelection = false;

            string facing = Variant?["facing"] ?? string.Empty;
            if (!facing.Equals("north", StringComparison.OrdinalIgnoreCase))
            {
                CreativeInventoryTabs = Array.Empty<string>();
                CreativeInventoryStacks = Array.Empty<CreativeTabAndStackList>();
            }
        }

        // Block's default held-item tooltip doesn't pick up "blockdesc-" the way Item's does with
        // "itemdesc-" (that key is only consumed by GetPlacedBlockInfo), so add it explicitly.
        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("photocore:blockdesc-platebox"));
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null)
            {
                return [stack];
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null) return stack;
            return base.OnPickBlock(world, pos);
        }

        private ItemStack? CreateDropStack(IWorldAccessor world, BlockPos pos)
        {
            Block? closedBlock = world?.GetBlock(_closedBoxCode);
            ItemStack stack = closedBlock != null ? new ItemStack(closedBlock) : new ItemStack(this);
            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityPlateBox be)
            {
                be.SaveToItemStack(stack);
            }

            return stack;
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world == null || pos == null)
            {
                return;
            }

            if (world.Side == EnumAppSide.Server && (byPlayer == null || byPlayer.WorldData?.CurrentGameMode != EnumGameMode.Creative))
            {
                ItemStack? stack = CreateDropStack(world, pos);
                if (stack != null)
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                world.PlaySoundAt(Sounds?.GetBreakSound(byPlayer).Location, pos, 0.0, byPlayer);
            }

            SpawnBlockBrokenParticles(pos, byPlayer);
            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.RemoveBlockEntity(pos);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null!)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);

            if (world?.BlockAccessor?.GetBlockEntity(blockPos) is not BlockEntityPlateBox be) return;

            // SetBlock() is used to swap closed/open variants; those transitions should not trigger placement SFX.
            if (byItemStack == null) return;

            be.LoadFromItemStack(byItemStack, world);

            be.SetOpen(false);

            if (world?.Side == EnumAppSide.Server)
            {
                world.PlaySoundAt(_woodThudSound, blockPos.X + 0.5, blockPos.Y + 0.5, blockPos.Z + 0.5, null, true, 16f, 1f);
                PlayPlateThuds(world, blockPos, be.PlateCount);
            }
        }

        private const int PlateThudStaggerMinMs = 18;
        private const int PlateThudStaggerRangeMs = 25;

        // A randomized glass thud per stored plate, layered on the wood-thud, so a loaded box lands
        // audibly heavier than an empty one. Random file + pitch per plate keep it from ringing as one
        // tone, and staggering the delays (rather than firing all at once) reads as plates settling
        // instead of a single simultaneous clatter.
        private static void PlayPlateThuds(IWorldAccessor world, BlockPos pos, int plateCount)
        {
            double x = pos.X + 0.5, y = pos.Y + 0.5, z = pos.Z + 0.5;
            int delayMs = 0;

            for (int i = 0; i < plateCount; i++)
            {
                AssetLocation sound = _glassThudSounds[world.Rand.Next(_glassThudSounds.Length)];
                float pitch = 0.92f + (float)world.Rand.NextDouble() * 0.16f;
                PlaySoundWithDelay(world, x, y, z, sound, delayMs, pitch, 0.7f);
                delayMs += PlateThudStaggerMinMs + world.Rand.Next(PlateThudStaggerRangeMs);
            }
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            string? currentFacing = Variant?["facing"];
            BlockFacing playerFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.Pos?.Yaw ?? 0f);
            string desiredFacing = playerFacing.Opposite.Code;

            // If already the right variant (or no facing variant), place directly.
            if (string.IsNullOrEmpty(currentFacing) || currentFacing == desiredFacing)
                return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);

            Block? facingBlock = world.GetBlock(new AssetLocation("photocore", "platebox-" + desiredFacing));
            if (facingBlock == null || facingBlock.Id == 0)
                return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);

            // Delegate to the correct facing variant's TryPlaceBlock.
            // That call will hit the "currentFacing == desiredFacing" branch above, so no recursion.
            return facingBlock.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

    }
}

