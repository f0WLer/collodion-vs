using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public class BlockFramedPhotograph : BlockPhotographBase
    {
        private static readonly AssetLocation FramedItemCode = new AssetLocation("collodion:framedphotograph");

        private static bool TryGetOrientationSuffix(AssetLocation? code, out string side)
        {
            side = string.Empty;
            string path = code?.Path ?? string.Empty;
            int dash = path.LastIndexOf('-');
            if (dash < 0 || dash >= path.Length - 1) return false;

            side = path[(dash + 1)..];
            return !string.IsNullOrWhiteSpace(side);
        }

        private static void TryGiveOrDrop(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, ItemStack stack)
        {
            bool given = byPlayer.InventoryManager?.TryGiveItemstack(stack) ?? false;
            if (!given)
            {
                world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }

        private static bool IsShiftDown(IPlayer player)
        {
            EntityControls? controls = player?.Entity?.Controls;
            return controls?.ShiftKey == true || controls?.Sneak == true;
        }

        private static bool TryGetSingleFromDoubleVariant(Block? block, out AssetLocation singleCode)
        {
            singleCode = null!;

            AssetLocation? code = block?.Code;
            if (code == null) return false;

            string path = code.Path ?? string.Empty;
            if (path.StartsWith("framedphotographwall2-", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetOrientationSuffix(code, out string side)) return false;
                singleCode = new AssetLocation(code.Domain, $"framedphotographwall-{side}");
                return true;
            }

            if (path.StartsWith("framedphotographground2-", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetOrientationSuffix(code, out string side)) return false;
                singleCode = new AssetLocation(code.Domain, $"framedphotographground-{side}");
                return true;
            }

            return false;
        }

        private static bool TryCreateFramedPhotoStack(IWorldAccessor world, string photoId, string? framePlankBlockCode, string? caption, float exposureMovement, out ItemStack stack)
        {
            stack = null!;
            if (string.IsNullOrWhiteSpace(photoId)) return false;

            Item? framedItem = world.GetItem(FramedItemCode);
            if (framedItem == null) return false;

            ItemStack photoStack = new ItemStack(framedItem);
            photoStack.Attributes.SetString(PhotographAttrs.PhotoId, photoId);

            if (!string.IsNullOrWhiteSpace(framePlankBlockCode))
            {
                photoStack.Attributes.SetString(PhotographAttrs.FramePlank, framePlankBlockCode);
            }

            if (!string.IsNullOrWhiteSpace(caption))
            {
                photoStack.Attributes.SetString(PhotographAttrs.Caption, caption);
            }

            if (exposureMovement > 0f)
            {
                photoStack.Attributes.SetFloat(WetPlateAttrs.HoldStillMovement, exposureMovement);
            }

            stack = photoStack;
            return true;
        }

        private static bool TryGetFramePlankBlockCode(ItemStack? stack, IWorldAccessor world, out string plankBlockCode)
        {
            plankBlockCode = string.Empty;

            if (stack == null) return false;

            // Require a board item (not a block).
            if (stack.Block != null) return false;

            Item? item = stack.Item;
            AssetLocation? itemCode = item?.Code;
            if (itemCode == null) return false;

            string path = itemCode.Path ?? string.Empty;
            if (path.IndexOf("board", System.StringComparison.OrdinalIgnoreCase) < 0) return false;

            int dashIndex = path.LastIndexOf('-');
            if (dashIndex <= 0 || dashIndex >= path.Length - 1) return false;

            string woodCode = path[(dashIndex + 1)..];
            if (string.IsNullOrWhiteSpace(woodCode)) return false;

            // Try to resolve to a plank block with the same wood code.
            string domain = string.IsNullOrWhiteSpace(itemCode.Domain) ? "game" : itemCode.Domain;
            AssetLocation candidate = new AssetLocation(domain, $"planks-{woodCode}");
            Block? plankBlock = world.GetBlock(candidate);

            if (plankBlock == null || plankBlock.Id == 0)
            {
                // Fallback to game domain for base planks.
                candidate = new AssetLocation("game", $"planks-{woodCode}");
                plankBlock = world.GetBlock(candidate);
            }

            if (plankBlock == null || plankBlock.Id == 0) return false;

            plankBlockCode = plankBlock.Code.ToString();
            return true;
        }

        protected override AssetLocation PhotoItemCode => FramedItemCode;

        protected override string PlacedInfoName => "Framed Photograph";

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;

            // SHIFT + right-click on a 2-photo frame: pop the most recently added photo (Photo2)
            // and downgrade the block back to the 1-photo variant, preserving Photo1 state.
            if (IsShiftDown(byPlayer)
                && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPhotograph be
                && !string.IsNullOrWhiteSpace(be.PhotoId))
            {
                if (world.Side == EnumAppSide.Server)
                {
                    string photo1 = be.PhotoId ?? string.Empty;
                    string photo2 = be.PhotoId2 ?? string.Empty;
                    string caption = be.Caption ?? string.Empty;
                    string framePlank1 = be.FramePlankBlockCode ?? string.Empty;
                    string framePlank2 = be.FramePlankBlockCode2 ?? string.Empty;
                    float exposureMovement = be.ExposureMovement;

                    Block currentBlock = world.BlockAccessor.GetBlock(blockSel.Position);
                    AssetLocation singleCode = null!;
                    bool isDouble = !string.IsNullOrWhiteSpace(photo2)
                        && TryGetSingleFromDoubleVariant(currentBlock, out singleCode);

                    if (isDouble)
                    {
                        if (TryCreateFramedPhotoStack(world, photo2, framePlank2, null, exposureMovement, out ItemStack poppedStack))
                        {
                            TryGiveOrDrop(world, byPlayer, blockSel.Position, poppedStack);
                        }

                        Block? singleBlock = world.GetBlock(singleCode);
                        if (singleBlock != null && singleBlock.Id != 0)
                        {
                            world.BlockAccessor.SetBlock(singleBlock.Id, blockSel.Position);
                            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPhotograph newBe)
                            {
                                newBe.SetPhoto(photo1);
                                newBe.SetPhoto2(null);

                                if (!string.IsNullOrWhiteSpace(caption))
                                {
                                    newBe.SetCaption(caption);
                                }

                                if (!string.IsNullOrWhiteSpace(framePlank1))
                                {
                                    newBe.SetFramePlankBlockCode(framePlank1);
                                }
                                newBe.SetFramePlankBlockCode2(null);

                                if (exposureMovement > 0f)
                                {
                                    newBe.SetExposureMovement(exposureMovement);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (TryCreateFramedPhotoStack(world, photo1, framePlank1, caption, exposureMovement, out ItemStack removedStack))
                        {
                            TryGiveOrDrop(world, byPlayer, blockSel.Position, removedStack);
                        }

                        world.BlockAccessor.SetBlock(0, blockSel.Position);
                        world.BlockAccessor.RemoveBlockEntity(blockSel.Position);
                    }
                }

                return true;
            }

            // Caption editing remains right-click with writing item.
            if (IsWritingItem(held))
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            // Allow re-skinning the frame using any plank block.
            // Works for both ground-placed and wall-mounted frames.
            string path = Code?.Path ?? string.Empty;
            if (path.StartsWith("framedphotographground", System.StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("framedphotographwall", System.StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetFramePlankBlockCode(held, world, out string plankBlockCode))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPhotograph photoBe)
                        {
                            photoBe.SetFramePlankBlockCode(plankBlockCode);
                            if (!string.IsNullOrWhiteSpace(photoBe.PhotoId2))
                            {
                                photoBe.SetFramePlankBlockCode2(plankBlockCode);
                            }

                            bool isCreative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
                            if (!isCreative)
                            {
                                ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                                slot?.TakeOut(1);
                                slot?.MarkDirty();
                            }
                        }
                    }

                    // Prevent pickup while applying.
                    return true;
                }
            }

            // Non-shift right-click does not remove framed photos; adding is handled by held item interaction.
            return false;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            var interactions = new List<WorldInteraction>();

            Item? framedItem = world.GetItem(FramedItemCode);
            if (framedItem != null)
            {
                interactions.Add(new WorldInteraction
                {
                    ActionLangCode = "collodion:heldhelp-framedphoto-add",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new[] { new ItemStack(framedItem) }
                });
            }

            interactions.Add(new WorldInteraction
            {
                ActionLangCode = "collodion:heldhelp-framedphoto-remove",
                HotKeyCode = "sneak",
                MouseButton = EnumMouseButton.Right
            });

            return interactions.ToArray();
        }
    }
}
