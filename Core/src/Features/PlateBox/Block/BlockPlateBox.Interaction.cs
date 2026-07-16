using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Photocore.PlateBox
{
    public sealed partial class BlockPlateBox
    {
        // Keeps engine callback ownership local and delegates heavy interaction branching.
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel?.Position == null) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityPlateBox be) return false;
            return HandlePlateBoxInteractionStart(world, byPlayer, blockSel, be);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            Item? samplePlate = world.GetItem(_samplePlateCode);
            if (samplePlate == null)
            {
                return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            }

            return
            [
                new WorldInteraction
                {
                    ActionLangCode = "photocore:heldhelp-platebox-insert",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = [new ItemStack(samplePlate)]
                },
                new WorldInteraction
                {
                    ActionLangCode = "photocore:heldhelp-platebox-take",
                    MouseButton = EnumMouseButton.Right,
                    RequireFreeHand = true
                },
                new WorldInteraction
                {
                    ActionLangCode = "photocore:heldhelp-platebox-lid",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "shift"
                },
                new WorldInteraction
                {
                    ActionLangCode = "photocore:heldhelp-platebox-pickup",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCodes = ["shift", "ctrl"]
                }
            ];
        }

        private static readonly AssetLocation[] _plateSetSounds =
        [
            new("photocore", "sounds/glass-set1"),
            new("photocore", "sounds/glass-set2"),
            new("photocore", "sounds/glass-set3")
        ];
        
        private bool HandlePlateBoxInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityPlateBox be)
        {
            bool shift = IsShiftDown(byPlayer);

            // Ranked before the bare-shift lid toggle so the two-modifier combo isn't swallowed by it.
            if (shift && IsCtrlDown(byPlayer))
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TryPickupBoxAndGiveDrop(world, byPlayer, blockSel.Position);
            }

            // Close/open gets its own modifier so a plain click is always insert or take, never an
            // accidental close.
            if (shift)
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TrySetOpenState(world, blockSel.Position, be, open: !be.IsOpen);
            }

            if (!be.IsOpen)
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TrySetOpenState(world, blockSel.Position, be, open: true);
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            string blockFacing = world.BlockAccessor.GetBlock(blockSel.Position)?.Variant?["facing"] ?? "south";

            if (held != null && BlockEntityPlateBox.IsInsertablePlate(held))
            {
                if (world.Side == EnumAppSide.Client) return true;
                if (activeSlot == null) return true;

                int nearestSlot = GetNearestSlotIndex(blockSel, blockFacing);
                if (!be.TryInsertPlateAt(nearestSlot, held, world)) return true;

                activeSlot.TakeOut(1);
                activeSlot.MarkDirty();
                PlayRandomPlateSetSound(world, blockSel.Position, byPlayer);
                return true;
            }

            if (held == null)
            {
                int takeSlot = GetNearestOccupiedSlotIndex(be, blockSel, blockFacing);
                if (takeSlot < 0) return false;
                if (world.Side == EnumAppSide.Client) return true;

                ItemStack? taken = be.TakePlateAt(takeSlot, world);
                if (taken == null) return false;

                if (activeSlot != null && activeSlot.Itemstack == null)
                {
                    activeSlot.Itemstack = taken;
                    activeSlot.MarkDirty();
                    return true;
                }

                TryGiveOrSpawnStack(world, byPlayer, blockSel.Position, taken);
                PlayRandomPlateSetSound(world, blockSel.Position, byPlayer);
                return true;
            }

            return false;
        }

        // Nearest slot column by X to the click, so any click on the box lands a plate -- no precise aim.
        private static int GetNearestSlotIndex(BlockSelection blockSel, string facing)
        {
            if (blockSel?.HitPosition == null) return 0;

            (double hitX, _) = InverseFacingTransform(blockSel.HitPosition.X, blockSel.HitPosition.Z, facing);

            int nearest = 0;
            double nearestDist = double.MaxValue;

            for (int index = 0; index < _slotHitBoxes.Length; index++)
            {
                Cuboidf box = _slotHitBoxes[index];
                double dist = Math.Abs(hitX - ((box.X1 + box.X2) / 2.0));

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = index;
                }
            }

            return nearest;
        }

        // Like GetNearestSlotIndex but skips empty columns, so an empty-hand click grabs the closest
        // plate that actually exists. Returns -1 when the box holds no plates.
        private static int GetNearestOccupiedSlotIndex(BlockEntityPlateBox be, BlockSelection blockSel, string facing)
        {
            double hitX = 0.5;
            if (blockSel?.HitPosition != null)
                (hitX, _) = InverseFacingTransform(blockSel.HitPosition.X, blockSel.HitPosition.Z, facing);

            int nearest = -1;
            double nearestDist = double.MaxValue;

            for (int index = 0; index < _slotHitBoxes.Length; index++)
            {
                if (!be.HasPlateAt(index)) continue;

                Cuboidf box = _slotHitBoxes[index];
                double dist = Math.Abs(hitX - ((box.X1 + box.X2) / 2.0));

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = index;
                }
            }

            return nearest;
        }

        private static (double, double) InverseFacingTransform(double x, double z, string facing)
        {
            return facing switch
            {
                "east" => (z, 1.0 - x),
                "north" => (x, 1.0 - z),
                "west" => (1.0 - z, x),
                _ => (1.0 - x, z)
            };
        }

        private static bool IsShiftDown(IPlayer player)
        {
            var controls = player?.Entity?.Controls;
            return controls?.ShiftKey == true || controls?.Sneak == true;
        }

        // ShiftKey/CtrlKey are the separable mouse-interaction modifiers the engine syncs for exactly
        // this (see EntityControls docs); Sneak/Sprint are movement actions and not read here.
        private static bool IsCtrlDown(IPlayer player)
        {
            return player?.Entity?.Controls?.CtrlKey == true;
        }

        // Plays a randomized glass set/remove sound to avoid repetitive slot foley.
        private static void PlayRandomPlateSetSound(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer)
        {
            if (world == null || pos == null || world.Side != EnumAppSide.Server) return;

            int index = 0;
            float pitch = 1f;

            try
            {
                if (world.Rand != null)
                {
                    index = world.Rand.Next(_plateSetSounds.Length);
                    pitch = 0.92f + (float)world.Rand.NextDouble() * 0.16f;
                }
            }
            catch
            {
                index = 0;
                pitch = 1f;
            }

            AssetLocation sound = _plateSetSounds[index];
            world.PlaySoundAt(sound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, pitch);
        }
    }
}