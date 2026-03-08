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

            // Accept held collectibles (item or block) for plank-like resources,
            // but keep board BLOCKS excluded per interaction design.
            AssetLocation? itemCode = stack.Collectible?.Code;
            if (itemCode == null) return false;

            string path = itemCode.Path ?? string.Empty;
            string lowerPath = path.ToLowerInvariant();

            bool isBoardLike = lowerPath.StartsWith("board-", System.StringComparison.OrdinalIgnoreCase)
                || lowerPath.StartsWith("boards-", System.StringComparison.OrdinalIgnoreCase);
            bool isPlankLike = lowerPath.StartsWith("plank-", System.StringComparison.OrdinalIgnoreCase)
                || lowerPath.StartsWith("planks-", System.StringComparison.OrdinalIgnoreCase);

            if (!isBoardLike && !isPlankLike)
            {
                return false;
            }

            // Keep board BLOCKS excluded; frame skinning uses held items.
            if (stack.Block != null && isBoardLike)
            {
                return false;
            }

            string domain = string.IsNullOrWhiteSpace(itemCode.Domain) ? "game" : itemCode.Domain;

            var candidatePaths = new List<string>();

            // Direct plank item path variants.
            if (lowerPath.StartsWith("planks-", System.StringComparison.OrdinalIgnoreCase))
            {
                candidatePaths.Add(path);
            }
            else if (lowerPath.StartsWith("plank-", System.StringComparison.OrdinalIgnoreCase))
            {
                candidatePaths.Add("planks-" + path.Substring("plank-".Length));
            }

            // Board item path variants.
            if (lowerPath.StartsWith("board-", System.StringComparison.OrdinalIgnoreCase))
            {
                candidatePaths.Add("planks-" + path.Substring("board-".Length));
            }
            else if (lowerPath.StartsWith("boards-", System.StringComparison.OrdinalIgnoreCase))
            {
                candidatePaths.Add("planks-" + path.Substring("boards-".Length));
            }

            // Fallbacks: try suffixes after any board/plank marker and then trailing tokens.
            string[] markers = { "board-", "boards-", "plank-", "planks-" };
            foreach (string marker in markers)
            {
                int idx = lowerPath.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                string suffix = path.Substring(idx + marker.Length);
                if (!string.IsNullOrWhiteSpace(suffix))
                {
                    candidatePaths.Add("planks-" + suffix);
                }

                string[] parts = suffix.Split('-');
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (string.IsNullOrWhiteSpace(parts[i])) continue;
                    candidatePaths.Add("planks-" + parts[i]);
                }
            }

            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string candidatePath in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(candidatePath)) continue;
                if (!seen.Add(candidatePath)) continue;

                var tryPaths = new List<string>();

                // Base-game woodtyped planks use an orientation suffix (e.g. planks-acacia-ud).
                if (candidatePath.StartsWith("planks-", System.StringComparison.OrdinalIgnoreCase)
                    && !candidatePath.EndsWith("-ud", System.StringComparison.OrdinalIgnoreCase)
                    && !candidatePath.EndsWith("-ns", System.StringComparison.OrdinalIgnoreCase)
                    && !candidatePath.EndsWith("-we", System.StringComparison.OrdinalIgnoreCase))
                {
                    tryPaths.Add(candidatePath + "-ud");
                    tryPaths.Add(candidatePath + "-ns");
                    tryPaths.Add(candidatePath + "-we");
                }

                tryPaths.Add(candidatePath);

                Block? plankBlock = null;
                foreach (string tryPath in tryPaths)
                {
                    AssetLocation candidate = new AssetLocation(domain, tryPath);
                    plankBlock = world.GetBlock(candidate);

                    if (plankBlock == null || plankBlock.Id == 0)
                    {
                        // Fallback to game domain for base planks.
                        candidate = new AssetLocation("game", tryPath);
                        plankBlock = world.GetBlock(candidate);
                    }

                    if (plankBlock == null || plankBlock.Id == 0) continue;

                    break;
                }

                if (plankBlock == null || plankBlock.Id == 0) continue;

                plankBlockCode = plankBlock.Code.ToString();
                return true;
            }

            return false;
        }

        protected override AssetLocation PhotoItemCode => FramedItemCode;

        protected override string PlacedInfoName => "Framed Photograph";

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;

            // Plain RMB with board/plank item re-skins frame.
            // Shift+RMB remains reserved for pop/remove behavior below.
            if (!IsShiftDown(byPlayer) && TryGetFramePlankBlockCode(held, world, out string plankBlockCode))
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
                    string caption2 = be.Caption2 ?? string.Empty;
                    string framePlank1 = be.FramePlankBlockCode ?? string.Empty;
                    string framePlank2 = be.FramePlankBlockCode2 ?? string.Empty;
                    float exposureMovement = be.ExposureMovement;

                    Block currentBlock = world.BlockAccessor.GetBlock(blockSel.Position);
                    AssetLocation singleCode = null!;
                    bool isDouble = !string.IsNullOrWhiteSpace(photo2)
                        && TryGetSingleFromDoubleVariant(currentBlock, out singleCode);

                    if (isDouble)
                    {
                        if (TryCreateFramedPhotoStack(world, photo2, framePlank2, caption2, exposureMovement, out ItemStack poppedStack))
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
                                newBe.SetCaption2(null);

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
