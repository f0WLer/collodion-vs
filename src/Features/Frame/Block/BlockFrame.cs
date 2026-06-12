using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

using Collodion.PhotoMetadata.Model;

namespace Collodion.Frame
{
    // Photo-frame placeable block.
    // Holds a single photo plate (any stack carrying a non-empty PhotoId attribute).
    // Shift + right-click to remove; plain right-click with a photo plate to insert.
    // Rendering is handled by BlockEntityFrame via PhotoPlateRenderUtil + PhotoMeshUtil.
    public class BlockFrame : Block
    {
        private static bool IsShiftDown(IPlayer player)
        {
            EntityControls? controls = player?.Entity?.Controls;
            return controls?.ShiftKey == true || controls?.Sneak == true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (Code.Path.StartsWith("framedphotographground"))
            {
                BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.Pos?.Yaw ?? 0f).Opposite;
                string desiredPath = $"framedphotographground-{facing.Code}";
                if (!Code.Path.Equals(desiredPath, StringComparison.Ordinal))
                {
                    Block? target = world.GetBlock(new AssetLocation(Code.Domain, desiredPath));
                    if (target != null && target.Id != 0)
                        return target.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
                }
            }
            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityFrame be) return false;

            // Shift + right-click: remove stored photo and return it to the player.
            if (IsShiftDown(byPlayer))
            {
                if (be.Inventory == null || be.Inventory[0].Empty) return false;

                if (world.Side == EnumAppSide.Server)
                {
                    ItemStack? stored = be.Inventory[0].Itemstack?.Clone();
                    be.Inventory[0].TakeOutWhole();
                    be.MarkDirty(true);

                    if (stored != null && !byPlayer.InventoryManager.TryGiveItemstack(stored))
                    {
                        world.SpawnItemEntity(stored, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                    }
                }

                return true;
            }

            // Plain right-click with a photo plate: insert it.
            ItemSlot? heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            ItemStack? held = heldSlot?.Itemstack;
            if (held == null) return false;

            string photoId = held.Attributes?.GetString(PhotographAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return false;

            if (be.Inventory == null || !be.Inventory[0].Empty) return false;

            if (world.Side == EnumAppSide.Server)
            {
                int moved = heldSlot!.TryPutInto(world, be.Inventory[0]);
                if (moved > 0) be.MarkDirty(true);
            }

            return true;
        }

        // When the frame block is broken, drop the stored photo item separately.
        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFrame be
                && be.Inventory != null && !be.Inventory[0].Empty)
            {
                ItemStack? stored = be.Inventory[0].Itemstack?.Clone();
                be.Inventory[0].TakeOutWhole();
                if (stored != null)
                {
                    world.SpawnItemEntity(stored, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            string baseInfo = base.GetPlacedBlockInfo(world, pos, forPlayer);

            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFrame be
                || be.Inventory == null || be.Inventory[0].Empty)
            {
                return baseInfo;
            }

            string caption = be.Inventory[0].Itemstack?.Attributes?.GetString(PhotographAttrs.Caption) ?? string.Empty;
            string label = string.IsNullOrEmpty(caption) ? Lang.Get("collodion:frame-info-photograph") : caption;
            string line = Lang.Get("collodion:frame-info-displaying", label);
            return string.IsNullOrEmpty(baseInfo) ? line : baseInfo + "\n" + line;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            var helps = new List<WorldInteraction>();

            Item? photoPlate = world.GetItem(new AssetLocation("collodion", "photoplate"));
            if (photoPlate != null)
            {
                helps.Add(new WorldInteraction
                {
                    ActionLangCode = "collodion:heldhelp-frame-insert",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = [new ItemStack(photoPlate)]
                });
            }

            helps.Add(new WorldInteraction
            {
                ActionLangCode = "collodion:heldhelp-frame-take",
                HotKeyCode = "sneak",
                MouseButton = EnumMouseButton.Right
            });

            return helps.ToArray();
        }
    }
}
