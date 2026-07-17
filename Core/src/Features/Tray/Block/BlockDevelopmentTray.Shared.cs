using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using Photocore.Plates;
namespace Photocore.Tray
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

                // Past exposure the doubled cost is the gate, so those reclaim wet or dry -- throwing away
                // a good photo is the player's call to make. Earlier stages keep the original rule: the
                // glass is only recoverable once drying has already cost them the plate.
                bool eligible = IsOnDemandReclaimable(stage)
                    || (OnlyReclaimableOnceDried(stage) && PlateDryingTransition.IsDry(world, plateStack));

                if (eligible)
                {
                    plate = plateStack;
                    return true;
                }
            }

            plate = null!;
            return false;
        }

        private static bool OnlyReclaimableOnceDried(PlateStage stage)
            => stage == PlateStage.Sensitized || stage == PlateStage.Exposing;

        // Pre-development exposures still belong to whoever opened the shutter: the accumulating frames
        // live on that player's client and the plate still names them. Once developing, the exposure is
        // sealed into a photo on a shared object, so there is no session left to protect and any player
        // may reclaim it regardless of the setting.
        private bool CanReclaimOthersExposure(IPlayer byPlayer, ItemStack plate)
        {
            if (!IsPreDevelopmentExposure(PlateAttributes.GetStage(plate))) return true;
            if (Cfg?.PlateProcessing?.AllowReclaimingOthersExposures ?? true) return true;

            string? owner = plate.Attributes?.GetString(PlateAttributes.PhotographerUid);
            return string.IsNullOrEmpty(owner) || string.Equals(owner, byPlayer.PlayerUID, StringComparison.Ordinal);
        }
    }
}
