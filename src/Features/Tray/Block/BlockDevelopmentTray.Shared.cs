using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using Collodion.Plates;
namespace Collodion.Tray
{
    public sealed partial class BlockDevelopmentTray
    {
        private static bool IsHoldingChemical(ItemSlot? slot, AssetLocation code)
        {
            return slot?.Itemstack != null && PlateChemicalUtil.IsChemicalOrContainerWith(slot.Itemstack, code);
        }

        private static bool TryGetDeveloperPourContext(BlockEntityDevelopmentTray be, out ItemStack plate, out bool isExposed, out int currentPours)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null)
            {
                plate = null!;
                isExposed = false;
                currentPours = 0;
                return false;
            }

            plate = plateStack;

            PlateStage stage = PlateAttributes.GetStage(plate);
            isExposed = stage == PlateStage.Exposed || stage == PlateStage.ExposurePaused;

            currentPours = stage switch
            {
                PlateStage.Exposed => 0,
                PlateStage.ExposurePaused => 0,
                PlateStage.Developing => PlateAttributes.GetDevelopmentApplications(plate),
                PlateStage.Developed => RequiredDeveloperPours,
                _ => -1
            };

            if (currentPours < 0) return false;

            if (currentPours > RequiredDeveloperPours) currentPours = RequiredDeveloperPours;
            return true;
        }

        private static bool TryGetFixerPourContext(BlockEntityDevelopmentTray be, out ItemStack plate, out int pours)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null)
            {
                plate = null!;
                pours = 0;
                return false;
            }

            plate = plateStack;

            if (PlateAttributes.GetStage(plate) != PlateStage.Developed)
            {
                pours = 0;
                return false;
            }

            pours = RequiredDeveloperPours;
            return true;
        }

        private void SwapTrayBlockForPlateStage(IWorldAccessor world, BlockPos pos, string? stage, ItemStack? plateToKeep)
        {
            if (world == null || Code == null) return;

            string placementFacing = "east";
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityDevelopmentTray oldBe)
            {
                placementFacing = oldBe.PlacementFacingCode;
            }

            string path = Code.Path;
            if (!path.StartsWith("developmenttray-")) return;

            string rest = path.Substring("developmenttray-".Length);
            int dash = rest.IndexOf('-');
            string clay = dash >= 0 ? rest.Substring(0, dash) : rest;

            AssetLocation targetLoc = stage == null
                ? new AssetLocation(Code.Domain, $"developmenttray-{clay}")
                : new AssetLocation(Code.Domain, $"developmenttray-{clay}-{stage}");

            Block? target = world.GetBlock(targetLoc);
            if (target == null || target.Id <= 0) return;

            world.BlockAccessor.SetBlock(target.Id, pos);

            // Reapply the plate stack after swapping blocks (BE can be recreated).
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityDevelopmentTray newBe)
            {
                newBe.SetPlacementFacing(placementFacing, markBlockDirty: false);

                if (plateToKeep != null)
                {
                    newBe.TrySetPlate(plateToKeep);
                }
            }
            else if (plateToKeep != null)
            {
                world.SpawnItemEntity(plateToKeep, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }

        private static void Tell(IPlayer byPlayer, string message, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp)
            {
                sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        private static void GiveOrDrop(IWorldAccessor world, IPlayer byPlayer, ItemStack stack, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp)
            {
                if (!sp.InventoryManager.TryGiveItemstack(stack))
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
                return;
            }

            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        private static bool TryGetReclaimContext(BlockEntityDevelopmentTray be, IWorldAccessor world, out ItemStack plate)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack != null)
            {
                PlateStage stage = PlateAttributes.GetStage(plateStack);
                bool isWetStage = stage == PlateStage.Sensitized
                    || stage == PlateStage.Exposed
                    || stage == PlateStage.Developing
                    || stage == PlateStage.Developed;

                if (isWetStage && PlateDryingTransition.IsDry(world, plateStack))
                {
                    plate = plateStack;
                    return true;
                }
            }

            plate = null!;
            return false;
        }
    }
}