using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    public class ItemFramedPhotograph : ItemPhotograph
    {
        private static readonly AssetLocation FinishedPlateCode = new AssetLocation("collodion:finishedphotoplate");
        private static readonly AssetLocation FramedPhotoCode = new AssetLocation("collodion:framedphotograph");

        private static bool IsFramedPhotoBlock(Block? block)
        {
            string path = block?.Code?.Path ?? string.Empty;
            return path.StartsWith("framedphotographground", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("framedphotographwall", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetAdjacentFramePlacement(Block? existingBlock, BlockSelection blockSel, out BlockPos placePos, out AssetLocation blockCode)
        {
            placePos = null!;
            blockCode = null!;

            if (existingBlock?.Code == null || blockSel?.Position == null || blockSel.HitPosition == null) return false;
            if (!IsFramedPhotoBlock(existingBlock)) return false;

            string path = existingBlock.Code.Path ?? string.Empty;
            BlockFacing? offset = null;

            if (path.StartsWith("framedphotographground", StringComparison.OrdinalIgnoreCase))
            {
                double dx = blockSel.HitPosition.X - 0.5;
                double dz = blockSel.HitPosition.Z - 0.5;

                if (Math.Abs(dx) >= Math.Abs(dz))
                {
                    offset = dx >= 0 ? BlockFacing.EAST : BlockFacing.WEST;
                }
                else
                {
                    offset = dz >= 0 ? BlockFacing.SOUTH : BlockFacing.NORTH;
                }
            }
            else if (path.StartsWith("framedphotographwall", StringComparison.OrdinalIgnoreCase))
            {
                double dy = blockSel.HitPosition.Y - 0.5;
                int dash = path.LastIndexOf('-');
                string side = dash >= 0 && dash < path.Length - 1 ? path[(dash + 1)..] : string.Empty;

                if (side.Equals("north", StringComparison.OrdinalIgnoreCase) || side.Equals("south", StringComparison.OrdinalIgnoreCase))
                {
                    double dx = blockSel.HitPosition.X - 0.5;
                    if (Math.Abs(dy) >= Math.Abs(dx))
                    {
                        offset = dy >= 0 ? BlockFacing.UP : BlockFacing.DOWN;
                    }
                    else
                    {
                        offset = dx >= 0 ? BlockFacing.EAST : BlockFacing.WEST;
                    }
                }
                else if (side.Equals("east", StringComparison.OrdinalIgnoreCase) || side.Equals("west", StringComparison.OrdinalIgnoreCase))
                {
                    double dz = blockSel.HitPosition.Z - 0.5;
                    if (Math.Abs(dy) >= Math.Abs(dz))
                    {
                        offset = dy >= 0 ? BlockFacing.UP : BlockFacing.DOWN;
                    }
                    else
                    {
                        offset = dz >= 0 ? BlockFacing.SOUTH : BlockFacing.NORTH;
                    }
                }
            }

            if (offset == null) return false;

            placePos = blockSel.Position.AddCopy(offset);
            blockCode = existingBlock.Code;
            return true;
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, GridRecipe byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);

            ItemStack? outStack = outputSlot?.Itemstack;
            if (outStack == null) return;

            if (allInputslots == null) return;

            ItemStack? inPhoto = null;
            for (int i = 0; i < allInputslots.Length; i++)
            {
                ItemStack? s = allInputslots[i]?.Itemstack;
                if (s?.Collectible?.Code == null) continue;

                if (s.Collectible.Code.Domain == FinishedPlateCode.Domain && s.Collectible.Code.Path == FinishedPlateCode.Path)
                {
                    inPhoto = s;
                    break;
                }
            }

            // Normal craft: framed photo from finished plate.
            if (inPhoto != null)
            {
                try
                {
                    ITreeAttribute cloned = inPhoto.Attributes?.Clone() ?? new TreeAttribute();
                    outStack.Attributes = cloned;
                }
                catch
                {
                    // ignore
                }

                return;
            }

            // Reset craft: framed photo -> framed photo (clear dynamic frame override).
            ItemStack? inFrame = null;
            for (int i = 0; i < allInputslots.Length; i++)
            {
                ItemStack? s = allInputslots[i]?.Itemstack;
                if (s?.Collectible?.Code == null) continue;

                if (s.Collectible.Code.Domain == FramedPhotoCode.Domain && s.Collectible.Code.Path == FramedPhotoCode.Path)
                {
                    inFrame = s;
                    break;
                }
            }

            if (inFrame == null) return;

            try
            {
                ITreeAttribute cloned = inFrame.Attributes?.Clone() ?? new TreeAttribute();
                cloned.RemoveAttribute(PhotographAttrs.FramePlank);
                outStack.Attributes = cloned;
            }
            catch
            {
                // ignore
            }
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (blockSel == null || api.World == null) return;

            handling = EnumHandHandling.PreventDefault;

            if (api.Side != EnumAppSide.Server)
            {
                return;
            }

            ItemStack? stack = slot.Itemstack;
            if (stack == null) return;

            string photoId = stack.Attributes.GetString(PhotographAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return;

            var world = api.World;
            if (world == null) return;

            BlockPos pos = blockSel.Position;
            BlockFacing face = blockSel.Face;

            AssetLocation blockCode;
            BlockPos placePos;

            Block clickedBlock = world.BlockAccessor.GetBlock(pos);
            if (TryGetAdjacentFramePlacement(clickedBlock, blockSel, out BlockPos adjacentPos, out AssetLocation adjacentCode))
            {
                placePos = adjacentPos;
                blockCode = adjacentCode;
            }
            else
            {
                // Wall placement (like mounted photographs)
                if (face == BlockFacing.NORTH || face == BlockFacing.EAST || face == BlockFacing.SOUTH || face == BlockFacing.WEST)
                {
                    placePos = pos.AddCopy(face);
                    string orientation = face.Opposite.Code; // face outward from wall
                    blockCode = new AssetLocation("collodion", $"framedphotographwall-{orientation}");
                }
                // Ground placement (place on top of a block)
                else if (face == BlockFacing.UP)
                {
                    placePos = pos.UpCopy();

                    // Face toward player
                    BlockFacing playerFacing = BlockFacing.HorizontalFromAngle((float)byEntity.Pos.Yaw);
                    string orientation = playerFacing.Opposite.Code;
                    blockCode = new AssetLocation("collodion", $"framedphotographground-{orientation}");
                }
                else
                {
                    return;
                }
            }

            Block framedBlock = world.GetBlock(blockCode);
            if (framedBlock == null)
            {
                return;
            }

            Block existingBlock = world.BlockAccessor.GetBlock(placePos);
            if (existingBlock.Id != 0 && !existingBlock.IsReplacableBy(framedBlock))
            {
                return;
            }

            world.BlockAccessor.SetBlock(framedBlock.Id, placePos);

            if (world.BlockAccessor.GetBlockEntity(placePos) is BlockEntityPhotograph be)
            {
                be.SetPhoto(photoId);

                try
                {
                    string caption = stack.Attributes.GetString(PhotographAttrs.Caption) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(caption))
                    {
                        be.SetCaption(caption);
                    }
                }
                catch
                {
                    // ignore
                }

                try
                {
                    string framePlank = stack.Attributes.GetString(PhotographAttrs.FramePlank) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(framePlank))
                    {
                        be.SetFramePlankBlockCode(framePlank);
                    }
                }
                catch
                {
                    // ignore
                }

                try
                {
                    float movement = stack.Attributes.GetFloat(WetPlateAttrs.HoldStillMovement, 0f);
                    if (movement > 0f)
                    {
                        be.SetExposureMovement(movement);
                    }
                }
                catch
                {
                    // ignore
                }
            }

            slot.TakeOut(1);
            slot.MarkDirty();
        }
    }
}
