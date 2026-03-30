using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public abstract class BlockPhotographBase : Block
    {
        private GuiDialogPhotographCaption? openCaptionDialog;

        protected abstract AssetLocation PhotoItemCode { get; }

        protected virtual string PlacedInfoName => "Photograph";

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (TryCreatePhotoStack(world, pos, out ItemStack stack))
            {
                return new[] { stack };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if (TryCreatePhotoStack(world, pos, out ItemStack stack))
            {
                return stack;
            }

            return base.OnPickBlock(world, pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel?.Position == null) return false;

            // Caption editing (sign-like UX): right-click with pigment/charcoal.
            if (IsWritingItem(byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack))
            {
                // Client: open the editor. Server: consume to prevent pickup.
                if (world.Side == EnumAppSide.Client && world.Api is ICoreClientAPI capi)
                {
                    string initial = string.Empty;
                    if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPhotograph be)
                    {
                        initial = be.Caption ?? string.Empty;
                    }

                    try { openCaptionDialog?.TryClose(); }
                    catch (Exception ex) { capi.Logger.Warning("[Collodion] caption dialog close failed: {0}", ex.Message); }
                    openCaptionDialog = new GuiDialogPhotographCaption(capi, blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, initial);
                    openCaptionDialog.TryOpen();
                }

                return true;
            }

            // Pick up the photograph when clicked.
            if (TryCreatePhotoStack(world, blockSel.Position, out ItemStack? stackToGive))
            {
                if (!byPlayer.InventoryManager.TryGiveItemstack(stackToGive))
                {
                    world.SpawnItemEntity(stackToGive, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                world.BlockAccessor.SetBlock(0, blockSel.Position);
                world.BlockAccessor.RemoveBlockEntity(blockSel.Position);
                return true;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPhotograph be && !string.IsNullOrEmpty(be.PhotoId))
            {
                return PlacedInfoName;
            }

            return base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        private bool TryCreatePhotoStack(IWorldAccessor world, BlockPos pos, out ItemStack stack)
        {
            stack = null!;

            if (world?.BlockAccessor?.GetBlockEntity(pos) is not BlockEntityPhotograph be) return false;
            if (string.IsNullOrEmpty(be.PhotoId)) return false;

            Item? photoItem = world.GetItem(PhotoItemCode);
            if (photoItem == null) return false;

            ItemStack s = new ItemStack(photoItem);
            s.Attributes.SetString(PhotographAttrs.PhotoId, be.PhotoId);

            if (!string.IsNullOrEmpty(be.Caption))
            {
                s.Attributes.SetString(PhotographAttrs.Caption, be.Caption);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(be.FramePlankBlockCode))
                {
                    s.Attributes.SetString(PhotographAttrs.FramePlank, be.FramePlankBlockCode);
                }

                if (be.ExposureMovement > 0f)
                {
                    s.Attributes.SetDouble(WetPlateAttrs.HoldStillMovement, be.ExposureMovement);
                }
            }
            catch
            {
                // ignore
            }

            stack = s;
            return true;
        }

        protected static bool IsWritingItem(ItemStack? stack)
        {
            if (stack?.ItemAttributes == null) return false;

            try
            {
                // Vanilla pigments/charcoal usually advertise a "pigment" attribute.
                var pigment = stack.ItemAttributes["pigment"];
                if (pigment.Exists) return true;
                if (pigment.AsBool(false)) return true;
            }
            catch
            {
                // ignore
            }

            try
            {
                string path = stack.Collectible?.Code?.Path ?? string.Empty;
                if (path.IndexOf("charcoal", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch
            {
                // ignore
            }

            return false;
        }
    }
}
