using Vintagestory.API.Common;

namespace Collodion
{
    public class BlockFramedPhotograph : BlockPhotographBase
    {
        private static readonly AssetLocation FramedItemCode = new AssetLocation("collodion:framedphotograph");

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
            // Allow re-skinning the frame using any plank block.
            // Works for both ground-placed and wall-mounted frames.
            string path = Code?.Path ?? string.Empty;
            if (path.StartsWith("framedphotographground", System.StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("framedphotographwall", System.StringComparison.OrdinalIgnoreCase))
            {
                ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (TryGetFramePlankBlockCode(held, world, out string plankBlockCode))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPhotograph be)
                        {
                            be.SetFramePlankBlockCode(plankBlockCode);

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

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
