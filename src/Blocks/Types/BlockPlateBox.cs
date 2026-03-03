using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public sealed class BlockPlateBox : Block
    {
        private static readonly AssetLocation SamplePlateCode = new AssetLocation("collodion", "silveredplate");
        private static readonly AssetLocation ClosedBoxCode = new AssetLocation("collodion", "platebox");
        private static readonly AssetLocation OpenBoxCode = new AssetLocation("collodion", "platebox-open");
        private static readonly Cuboidf[] SlotHitBoxes =
        {
            // Matches platehb1..platehb8 in assets/collodion/shapes/block/platebox-open.json
            new Cuboidf(1.5f / 16f, 0.5f / 16f, 4.5f / 16f, 2.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new Cuboidf(3.0f / 16f, 0.5f / 16f, 4.5f / 16f, 3.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new Cuboidf(4.5f / 16f, 0.5f / 16f, 4.5f / 16f, 5.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new Cuboidf(6.0f / 16f, 0.5f / 16f, 4.5f / 16f, 6.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new Cuboidf(9.5f / 16f, 0.5f / 16f, 4.5f / 16f, 10.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new Cuboidf(11.0f / 16f, 0.5f / 16f, 4.5f / 16f, 11.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new Cuboidf(12.5f / 16f, 0.5f / 16f, 4.5f / 16f, 13.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new Cuboidf(14.0f / 16f, 0.5f / 16f, 4.5f / 16f, 14.5f / 16f, 8.2f / 16f, 11.5f / 16f)
        };

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            PartialSelection = false;
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
#pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
#pragma warning restore CS0618

            try
            {
                var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();

#pragma warning disable CS0618
                string poseKey = target switch
                {
                    EnumItemRenderTarget.HandFp => "platebox-fp",
                    EnumItemRenderTarget.HandTp => "platebox-tp",
                    EnumItemRenderTarget.Gui => "platebox-gui",
                    EnumItemRenderTarget.Ground => "platebox-ground",
                    _ => string.Empty
                };
#pragma warning restore CS0618

                if (!string.IsNullOrWhiteSpace(poseKey))
                {
                    RenderPoseUtil.ApplyPoseDelta(modSys, poseKey, ref renderinfo);
                }
            }
            catch
            {
                // Keep rendering functional even if pose tuning fails.
            }
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null)
            {
                return new[] { stack };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null) return stack;
            return base.OnPickBlock(world, pos);
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

                world.PlaySoundAt(Sounds?.GetBreakSound(byPlayer), pos, 0.0, byPlayer);
            }

            SpawnBlockBrokenParticles(pos, byPlayer);
            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.RemoveBlockEntity(pos);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null!)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);

            if (world?.BlockAccessor?.GetBlockEntity(blockPos) is not BlockEntityPlateBox be) return;

            if (byItemStack != null)
            {
                be.LoadFromItemStack(byItemStack, world);
            }

            be.SetOpen(false);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel?.Position == null) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityPlateBox be) return false;

            // Shift + RMB always picks up the box, regardless of open/closed state or stored contents.
            // The created item stack carries serialized slot data via CreateDropStack().
            if (IsShiftDown(byPlayer))
            {
                if (world.Side == EnumAppSide.Client) return true;

                ItemStack? stack = CreateDropStack(world, blockSel.Position);
                if (stack != null)
                {
                    bool given = byPlayer.InventoryManager?.TryGiveItemstack(stack) ?? false;
                    if (!given)
                    {
                        world.SpawnItemEntity(stack, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                    }
                }

                world.BlockAccessor.SetBlock(0, blockSel.Position);
                world.BlockAccessor.RemoveBlockEntity(blockSel.Position);
                return true;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;

            // Closed box: first right-click always opens.
            if (!be.IsOpen)
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TrySetOpenState(world, blockSel.Position, be, open: true);
            }

            int slotIndex = GetSlotIndexFromHit(blockSel);
            bool clickedSlot = slotIndex >= 0;

            // Open box body click toggles closed.
            if (!clickedSlot)
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TrySetOpenState(world, blockSel.Position, be, open: false);
            }

            if (held != null && BlockEntityPlateBox.IsInsertablePlate(held))
            {
                if (world.Side == EnumAppSide.Client)
                {
                    return be.CanInsertAt(slotIndex);
                }

                if (activeSlot == null || !be.TryInsertPlateAt(slotIndex, held, world)) return false;

                activeSlot.TakeOut(1);
                activeSlot.MarkDirty();
                return true;
            }

            // Empty hand + empty slot: pull first plate from inventory and insert.
            if (held == null && !be.HasPlateAt(slotIndex))
            {
                if (world.Side == EnumAppSide.Client)
                {
                    return true;
                }

                if (!be.CanInsertAt(slotIndex)) return false;

                if (TryFindFirstPlateSlot(byPlayer, out ItemSlot? sourceSlot) && sourceSlot?.Itemstack != null)
                {
                    ItemStack sourceStack = sourceSlot.Itemstack;
                    if (be.TryInsertPlateAt(slotIndex, sourceStack, world))
                    {
                        sourceSlot.TakeOut(1);
                        sourceSlot.MarkDirty();
                        return true;
                    }
                }

                return false;
            }

            if (held == null)
            {
                if (world.Side == EnumAppSide.Client)
                {
                    return be.HasPlateAt(slotIndex);
                }

                ItemStack? taken = be.TakePlateAt(slotIndex, world);
                if (taken == null) return false;

                if (activeSlot?.Itemstack == null)
                {
                    activeSlot!.Itemstack = taken;
                    activeSlot.MarkDirty();
                    return true;
                }

                bool given = byPlayer.InventoryManager?.TryGiveItemstack(taken) ?? false;
                if (!given)
                {
                    world.SpawnItemEntity(taken, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                return true;
            }

            return false;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            Item? samplePlate = world.GetItem(SamplePlateCode);
            if (samplePlate == null)
            {
                return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            }

            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "collodion:heldhelp-platebox-insert",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new[] { new ItemStack(samplePlate) }
                },
                new WorldInteraction
                {
                    ActionLangCode = "collodion:heldhelp-platebox-take",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }

        private static bool TryFindFirstPlateSlot(IPlayer player, out ItemSlot? slot)
        {
            slot = null;

            IPlayerInventoryManager? inv = player?.InventoryManager;
            if (inv == null) return false;

            foreach (InventoryBase inventory in inv.InventoriesOrdered)
            {
                if (inventory == null || inventory.Empty) continue;

                for (int index = 0; index < inventory.Count; index++)
                {
                    ItemSlot? candidate = inventory[index];
                    if (candidate?.Itemstack == null) continue;
                    if (!BlockEntityPlateBox.IsInsertablePlate(candidate.Itemstack)) continue;

                    slot = candidate;
                    return true;
                }
            }

            return false;
        }

        private ItemStack? CreateDropStack(IWorldAccessor world, BlockPos pos)
        {
            Block? closedBlock = world?.GetBlock(ClosedBoxCode);
            ItemStack stack = closedBlock != null ? new ItemStack(closedBlock) : new ItemStack(this);
            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityPlateBox be)
            {
                be.SaveToItemStack(stack);
            }

            return stack;
        }

        private static int GetSlotIndexFromHit(BlockSelection blockSel)
        {
            if (blockSel?.HitPosition == null) return -1;

            double hitX = blockSel.HitPosition.X;
            double hitY = blockSel.HitPosition.Y;
            double hitZ = blockSel.HitPosition.Z;

            // Slight tolerance so authored boxes remain easy to hit in-world.
            const double pad = 0.01;

            for (int index = 0; index < SlotHitBoxes.Length; index++)
            {
                Cuboidf box = SlotHitBoxes[index];

                if (hitX < box.X1 - pad || hitX > box.X2 + pad) continue;
                if (hitY < box.Y1 - pad || hitY > box.Y2 + pad) continue;
                if (hitZ < box.Z1 - pad || hitZ > box.Z2 + pad) continue;

                return index;
            }

            return -1;
        }

        private static bool IsShiftDown(IPlayer player)
        {
            var controls = player?.Entity?.Controls;
            return controls?.ShiftKey == true || controls?.Sneak == true;
        }

        private static bool TrySetOpenState(IWorldAccessor world, BlockPos pos, BlockEntityPlateBox be, bool open)
        {
            if (world == null || pos == null || be == null) return false;

            AssetLocation targetCode = open ? OpenBoxCode : ClosedBoxCode;
            Block? target = world.GetBlock(targetCode);
            if (target == null)
            {
                return be.SetOpen(open) || be.IsOpen == open;
            }

            if (world.BlockAccessor.GetBlock(pos)?.Code == targetCode)
            {
                return be.SetOpen(open) || be.IsOpen == open;
            }

            var snapshot = new TreeAttribute();
            be.SetOpen(open);
            be.ToTreeAttributes(snapshot);

            world.BlockAccessor.SetBlock(target.Id, pos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPlateBox newBe)
            {
                newBe.FromTreeAttributes(snapshot, world);
                newBe.MarkDirty(true);
                try { world.BlockAccessor.MarkBlockEntityDirty(pos); } catch { }
                try { world.BlockAccessor.MarkBlockDirty(pos); } catch { }
                return true;
            }

            return false;
        }
    }
}
