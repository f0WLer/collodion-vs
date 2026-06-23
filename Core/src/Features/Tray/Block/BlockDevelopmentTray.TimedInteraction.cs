using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

using Photochemistry.Plates;
namespace Photochemistry.Tray
{
    public sealed partial class BlockDevelopmentTray
    {
        private static readonly AssetLocation _glassPlateItemCode = new("photochemistry", "glassplate");
        private static readonly AssetLocation _fizzSound = new("photochemistry", "sounds/fizz");


        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;
            return HandleTrayRuntimeTimedInteractionStep(secondsUsed, world, byPlayer, blockSel.Position);
        }

        // Clears tray timed state on stop while preserving the RMB-release latch for completed client actions.
        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null) return;

            // If a timed action completed, latch until RMB is actually released.
            // (OnBlockInteractStop may fire both on completion and on RMB release, so do NOT clear here.)
            try
            {
                if (world?.Side == EnumAppSide.Client && blockSel?.Position != null)
                {
                    BlockPos pos = blockSel.Position;
                    if (TryResolveTimedActionKind(byPlayer, pos, out TrayActionKind actionKind) && secondsUsed >= GetDurationSeconds(actionKind))
                    {
                        TrayTimedInteractionState.SetNeedsRelease(byPlayer);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(world.Logger, "OnBlockInteractStop timed-release check failed: {0}", ex.Message);
            }

            TrayTimedInteractionState.Clear(byPlayer);
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        private bool HandleTrayRuntimeTimedInteractionStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (!TryResolveTimedActionKind(byPlayer, pos, out TrayActionKind actionKind))
            {
                return false;
            }

            // Tray block entity must still exist while the timed action is running.
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityDevelopmentTray be)
            {
                TrayTimedInteractionState.Clear(byPlayer);
                return false;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (!TryValidateTimedStepForAction(world, byPlayer, pos, be, activeSlot, actionKind, out ItemStack plate, out bool isExposed, out int currentApplications))
            {
                TrayTimedInteractionState.Clear(byPlayer);
                return false;
            }

            if (secondsUsed < GetDurationSeconds(actionKind)) return true;

            // Latch until RMB release to prevent auto-starting the next action.
            if (world.Side == EnumAppSide.Client) TrayTimedInteractionState.SetNeedsRelease(byPlayer);

            if (world.Side == EnumAppSide.Server)
            {
                if (!TryApplyTimedActionServer(world, byPlayer, pos, be, activeSlot, plate, isExposed, currentApplications, actionKind))
                {
                    TrayTimedInteractionState.Clear(byPlayer);
                    return false;
                }

                world.PlaySoundAt(_fizzSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, 1f);
            }

            TrayTimedInteractionState.Clear(byPlayer);
            return false;
        }

        private static bool TryResolveTimedActionKind(IPlayer byPlayer, BlockPos pos, out TrayActionKind actionKind)
        {
            if (TrayTimedInteractionState.IsActive(byPlayer, pos, ActionDeveloper))
            {
                actionKind = TrayActionKind.Developer;
                return true;
            }

            if (TrayTimedInteractionState.IsActive(byPlayer, pos, ActionFixer))
            {
                actionKind = TrayActionKind.Fixer;
                return true;
            }

            if (TrayTimedInteractionState.IsActive(byPlayer, pos, ActionWater))
            {
                actionKind = TrayActionKind.Water;
                return true;
            }

            actionKind = default;
            return false;
        }

        private float GetDurationSeconds(TrayActionKind actionKind)
        {
            return actionKind switch
            {
                TrayActionKind.Developer => GetDeveloperPourSeconds(),
                TrayActionKind.Fixer => GetFixerPourSeconds(),
                _ => GetWaterPourSeconds()
            };
        }

        private bool TryValidateTimedStepForAction(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, TrayActionKind actionKind, out ItemStack plate, out bool isExposed, out int currentApplications)
        {
            plate = null!;
            isExposed = false;
            currentApplications = 0;

            AssetLocation portionCode = GetPortionCode(actionKind);
            int amountPerUse = GetChemicalUnitsPerUse();
            if (!IsHoldingChemical(activeSlot, portionCode)) return false;
            if (!PlateChemicalUtil.HasConsumableChemical(activeSlot, portionCode, amountPerUse))
            {
                if (world.Side == EnumAppSide.Server)
                {
                    Tell(byPlayer, GetMissingChemicalMessage(actionKind), pos);
                }

                return false;
            }

            switch (actionKind)
            {
                case TrayActionKind.Developer:
                    if (!TryGetDeveloperPourContext(be, out plate, out isExposed, out currentApplications)) return false;
                    return currentApplications < RequiredDeveloperPours;
                case TrayActionKind.Fixer:
                    if (!TryGetFixerPourContext(be, out plate, out currentApplications)) return false;
                    if (currentApplications < RequiredDeveloperPours)
                    {
                        if (world.Side == EnumAppSide.Server)
                        {
                            Tell(byPlayer, Lang.Get("photochemistry:msg-tray-underdeveloped", currentApplications, RequiredDeveloperPours), pos);
                        }

                        return false;
                    }

                    return true;
                default:
                    return TryGetReclaimContext(be, world, out plate);
            }
        }

        private bool TryApplyTimedActionServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate, bool isExposed, int currentApplications, TrayActionKind actionKind)
        {
            return actionKind switch
            {
                TrayActionKind.Developer => TryApplyDeveloperPourServer(world, byPlayer, pos, be, activeSlot, plate, isExposed, currentApplications),
                TrayActionKind.Fixer => TryApplyFixerPourServer(world, byPlayer, pos, be, activeSlot, plate),
                _ => TryApplyWaterPourServer(world, byPlayer, pos, activeSlot)
            };
        }

        private bool TryApplyDeveloperPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate, bool isExposed, int currentPours)
        {
            if (!PlateChemicalUtil.TryConsumeChemical(activeSlot, _developerPortionCode, GetChemicalUnitsPerUse()))
            {
                Tell(byPlayer, GetMissingChemicalMessage(TrayActionKind.Developer), pos);
                return false;
            }

            ItemStack newPlate = plate;
            if (isExposed)
            {
                // Developed output is declared by the exposed plate's itemtype ("developedItemCode"),
                // so a glass plate yields a photoplate and salted paper yields a paperprint — data-driven.
                AssetLocation developedCode = new(
                    plate.Collectible?.Attributes?["developedItemCode"]?.AsString(_photoPlateItemCode.ToString())
                    ?? _photoPlateItemCode.ToString());
                Item? photoPlateItem = world.GetItem(developedCode);
                if (photoPlateItem == null) return false;

                newPlate = new ItemStack(photoPlateItem);
                try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); }
                catch (Exception ex) { Log.Warn(world?.Logger, "TryApplyDeveloperPourServer: attribute merge failed: {0}", ex.Message); }
            }

            int newPours = currentPours + 1;
            if (newPours > RequiredDeveloperPours) newPours = RequiredDeveloperPours;

            bool developerComplete = newPours >= RequiredDeveloperPours;
            PlateAttributes.SetStage(newPlate, developerComplete ? PlateStage.Developed : PlateStage.Developing);
            PlateAttributes.SetDevelopmentApplications(newPlate, developerComplete ? 0 : newPours);

            PlateDryingTransition.ResetTimer(world!, newPlate, PlateDryingTransition.ResolveWetDurationHours(api, newPlate));

            be.TrySetPlate(newPlate);
            SwapTrayBlockForPlateStage(world!, pos, PlateAttributes.ToAttributeString(PlateAttributes.GetStage(newPlate)), newPlate);
            return true;
        }

        private bool TryApplyFixerPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate)
        {
            if (!PlateChemicalUtil.TryConsumeChemical(activeSlot, _fixerPortionCode, GetChemicalUnitsPerUse()))
            {
                Tell(byPlayer, GetMissingChemicalMessage(TrayActionKind.Fixer), pos);
                return false;
            }

            // Fixing only advances the stage, so keep the plate's current developed itemtype: a paper
            // print stays a paperprint, while a glass plate is already the photoplate item at this point.
            // Falling back to the configured glass photoplate only if the current collectible isn't an Item.
            Item? photoPlateItem = plate.Collectible as Item ?? world.GetItem(_photoPlateItemCode);
            if (photoPlateItem == null) return false;

            ItemStack finishedPlate = new ItemStack(photoPlateItem);
            try { finishedPlate.Attributes.MergeTree(plate.Attributes.Clone()); }
            catch (Exception ex) { Log.Warn(world?.Logger, "TryApplyFixerPourServer: attribute merge failed: {0}", ex.Message); }

            finishedPlate.Attributes.RemoveAttribute(PlateAttributes.PhotographerUid);
            PlateAttributes.SetStage(finishedPlate, PlateStage.Finished);
            PlateAttributes.ResetDevelopmentApplications(finishedPlate);

            be.TrySetPlate(finishedPlate);
            SwapTrayBlockForPlateStage(world!, pos, PlateAttributes.ToAttributeString(PlateStage.Finished), finishedPlate);
            return true;
        }

        // The tray empties immediately — rough plates cannot re-enter the development workflow.
        private bool TryApplyWaterPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, ItemSlot? activeSlot)
        {
            if (!PlateChemicalUtil.TryConsumeChemical(activeSlot, _waterPortionCode, GetChemicalUnitsPerUse()))
            {
                Tell(byPlayer, GetMissingChemicalMessage(TrayActionKind.Water), pos);
                return false;
            }

            Item? glassPlateItem = world.GetItem(_glassPlateItemCode);
            if (glassPlateItem == null) return false;

            ItemStack roughPlate = new ItemStack(glassPlateItem);
            PlateAttributes.SetStage(roughPlate, PlateStage.Rough);
            PlateAttributes.SetNameLangCode(roughPlate, "photochemistry:plate-name-glass");
            roughPlate.Attributes.SetString("plateBlockState", "rough");

            SwapTrayBlockForPlateStage(world, pos, null, null);
            GiveOrDrop(world, byPlayer, roughPlate, pos);
            return true;
        }

        private static AssetLocation GetPortionCode(TrayActionKind actionKind)
        {
            return actionKind switch
            {
                TrayActionKind.Developer => _developerPortionCode,
                TrayActionKind.Fixer => _fixerPortionCode,
                _ => _waterPortionCode
            };
        }

        private static string GetMissingChemicalMessage(TrayActionKind actionKind)
        {
            return actionKind switch
            {
                TrayActionKind.Developer => Lang.Get("photochemistry:msg-tray-need-developer"),
                TrayActionKind.Fixer => Lang.Get("photochemistry:msg-tray-need-fixer"),
                _ => Lang.Get("photochemistry:msg-tray-need-water")
            };
        }
    }
}