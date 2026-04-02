using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion
{
    public sealed partial class BlockDevelopmentTray
    {
        private bool HandleInteractStartServer(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack? held, int chemicalUnitsPerUse)
        {
            // Empty hand: take plate out.
            if (held == null)
            {
                if (!be.HasPlate) return false;

                ItemStack? taken = be.TakePlate();
                if (taken == null) return false;

                SwapTrayBlockForPlateStage(world, blockSel.Position, null, null);
                GiveOrDrop(world, byPlayer, taken, blockSel.Position);
                return true;
            }

            // Holding a plate: insert (only if tray empty). Dry plates still insertable for water rinse.
            if (IsInsertablePlate(held))
            {
                if (be.HasPlate) return false;
                if (activeSlot == null) return false;

                // Ensure tray photo orientation always tracks the player who is actively using the tray.
                // This acts as a reliable fallback if placement-time facing capture is unavailable.
                BlockFacing insertFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
                be.SetPlacementFacing(insertFacing.Code, markBlockDirty: false);

                ItemStack toInsert = held.Clone();
                toInsert.StackSize = 1;

                if (!be.TryInsertPlate(toInsert)) return false;

                PlateStateService.EnsureProcessId(toInsert);
                PlateStage trayStage = IsPlate(toInsert, ExposedPlateItemCode) ? PlateStage.Exposed : PlateStage.Developed;
                PlateStateService.EnsureStage(toInsert, trayStage);
                SwapTrayBlockForPlateStage(world, blockSel.Position, PlateStageUtil.ToAttributeString(trayStage), toInsert);

                activeSlot.TakeOut(1);
                activeSlot.MarkDirty();
                return true;
            }

            // Holding a dry silvered plate: insert for water rinse.
            if (IsPlate(held, SilveredPlateItemCode) && WetPlateAttrs.IsDry(world, held))
            {
                if (be.HasPlate) return false;
                if (activeSlot == null) return false;

                BlockFacing insertFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
                be.SetPlacementFacing(insertFacing.Code, markBlockDirty: false);

                ItemStack toInsert = held.Clone();
                toInsert.StackSize = 1;

                if (!be.TryInsertPlate(toInsert)) return false;

                PlateStateService.EnsureProcessId(toInsert);
                PlateStateService.EnsureStage(toInsert, PlateStage.Exposed);
                SwapTrayBlockForPlateStage(world, blockSel.Position, PlateStageUtil.ToAttributeString(PlateStage.Exposed), toInsert);

                activeSlot.TakeOut(1);
                activeSlot.MarkDirty();
                return true;
            }

            // Chemical interactions: resolve the plate's process to get the expected chemical codes.
            if (be.HasPlate)
            {
                DevelopmentParameters development = ResolveProcessDevelopment(be.PlateStack);
                AssetLocation devCode = new AssetLocation(development.DeveloperPortionCode);
                AssetLocation fixCode = new AssetLocation(development.FixerPortionCode);

                // Holding developer: start timed develop pour.
                if (IsHoldingChemical(activeSlot, devCode))
                {
                    if (!TryGetDeveloperPourContext(be, development.DeveloperPourCount, out ItemStack devPlate, out _, out _, out int currentPours)) return false;

                    if (currentPours >= development.DeveloperPourCount) return false;
                    if (WetPlateAttrs.IsDry(world, devPlate))
                    {
                        Tell(byPlayer, "Wetplate: the plate has dried and can no longer be used.", blockSel.Position);
                        return false;
                    }
                    if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, devCode, development.DeveloperAmountPerPour))
                    {
                        Tell(byPlayer, "Wetplate: need developer (at least 1 portion).", blockSel.Position);
                        return false;
                    }

                    world.PlaySoundAt(ChemicalPourSound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                    BeginTimed(byPlayer, blockSel.Position, ActionDeveloper, GetDeveloperPourSeconds());
                    return true;
                }

                // Holding fixer: start timed fix pour.
                if (IsHoldingChemical(activeSlot, fixCode))
                {
                    if (!TryGetFixerPourContext(be, development.DeveloperPourCount, out ItemStack fixPlate, out int pours)) return false;

                    if (WetPlateAttrs.IsDry(world, fixPlate))
                    {
                        Tell(byPlayer, "Wetplate: the plate has dried and can no longer be used.", blockSel.Position);
                        return false;
                    }
                    if (pours < development.DeveloperPourCount)
                    {
                        Tell(byPlayer, $"Wetplate: plate not fully developed ({pours}/{development.DeveloperPourCount}).", blockSel.Position);
                        return false;
                    }
                    if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, fixCode, development.FixerAmountPerPour))
                    {
                        Tell(byPlayer, "Wetplate: need fixer (at least 1 portion).", blockSel.Position);
                        return false;
                    }

                    world.PlaySoundAt(ChemicalPourSound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                    BeginTimed(byPlayer, blockSel.Position, ActionFixer, GetFixerPourSeconds());
                    return true;
                }
            }

            // Holding water: rinse a dry plate to reclaim rough glass (process-agnostic).
            if (IsHoldingChemical(activeSlot, WaterPortionCode))
            {
                if (!TryGetReclaimContext(be, world, out _)) return false;

                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, WaterPortionCode, chemicalUnitsPerUse))
                {
                    Tell(byPlayer, "Wetplate: need water (at least 1 portion).", blockSel.Position);
                    return false;
                }

                world.PlaySoundAt(ChemicalPourSound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                BeginTimed(byPlayer, blockSel.Position, ActionWater, GetWaterPourSeconds());
                return true;
            }

            return false;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;
            int chemicalUnitsPerUse = GetChemicalUnitsPerUse();

            BlockPos pos = blockSel.Position;

            // Timed developer pour.
            if (IsTimed(byPlayer, pos, ActionDeveloper))
            {
                if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityDevelopmentTray be) { ClearTimed(byPlayer); return false; }

                DevelopmentParameters development = ResolveProcessDevelopment(be.PlateStack);
                AssetLocation devCode = new AssetLocation(development.DeveloperPortionCode);

                ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                if (!IsHoldingChemical(activeSlot, devCode)) { ClearTimed(byPlayer); return false; }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, devCode, development.DeveloperAmountPerPour))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, "Wetplate: need developer (at least 1 portion).", pos);
                    }
                    ClearTimed(byPlayer);
                    return false;
                }

                if (!TryGetDeveloperPourContext(be, development.DeveloperPourCount, out ItemStack plate, out bool isExposed, out bool isDeveloped, out int currentPours))
                {
                    ClearTimed(byPlayer);
                    return false;
                }

                if (currentPours >= development.DeveloperPourCount) { ClearTimed(byPlayer); return false; }

                float duration = GetDeveloperPourSeconds();
                if (secondsUsed < duration) return true;

                // Latch until RMB release to prevent auto-starting the next pour.
                if (world.Side == EnumAppSide.Client) SetNeedsRelease(byPlayer);

                if (world.Side == EnumAppSide.Server)
                {
                    if (!TryApplyDeveloperPourServer(world, byPlayer, pos, be, activeSlot, plate, isExposed, currentPours, development))
                    {
                        ClearTimed(byPlayer);
                        return false;
                    }

                    world.PlaySoundAt(FizzSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, 1f);
                }

                ClearTimed(byPlayer);
                return false;
            }

            // Timed fixer pour.
            if (IsTimed(byPlayer, pos, ActionFixer))
            {
                if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityDevelopmentTray be) { ClearTimed(byPlayer); return false; }

                DevelopmentParameters development = ResolveProcessDevelopment(be.PlateStack);
                AssetLocation fixCode = new AssetLocation(development.FixerPortionCode);

                ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                if (!IsHoldingChemical(activeSlot, fixCode)) { ClearTimed(byPlayer); return false; }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, fixCode, development.FixerAmountPerPour))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, "Wetplate: need fixer (at least 1 portion).", pos);
                    }
                    ClearTimed(byPlayer);
                    return false;
                }

                if (!TryGetFixerPourContext(be, development.DeveloperPourCount, out ItemStack plate, out int pours))
                {
                    ClearTimed(byPlayer);
                    return false;
                }

                if (pours < development.DeveloperPourCount)
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, $"Wetplate: plate not fully developed ({pours}/{development.DeveloperPourCount}).", pos);
                    }
                    ClearTimed(byPlayer);
                    return false;
                }

                float duration = GetFixerPourSeconds();
                if (secondsUsed < duration) return true;

                // Latch until RMB release to prevent auto-starting the next action.
                if (world.Side == EnumAppSide.Client) SetNeedsRelease(byPlayer);

                if (world.Side == EnumAppSide.Server)
                {
                    if (!TryApplyFixerPourServer(world, byPlayer, pos, be, activeSlot, plate, development))
                    {
                        ClearTimed(byPlayer);
                        return false;
                    }

                    world.PlaySoundAt(FizzSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, 1f);
                }

                ClearTimed(byPlayer);
                return false;
            }

            // Timed water rinse.
            if (IsTimed(byPlayer, pos, ActionWater))
            {
                if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityDevelopmentTray be) { ClearTimed(byPlayer); return false; }

                ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                if (!IsHoldingChemical(activeSlot, WaterPortionCode)) { ClearTimed(byPlayer); return false; }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, WaterPortionCode, chemicalUnitsPerUse))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, "Wetplate: need water (at least 1 portion).", pos);
                    }
                    ClearTimed(byPlayer);
                    return false;
                }

                if (!TryGetReclaimContext(be, world, out ItemStack plate))
                {
                    ClearTimed(byPlayer);
                    return false;
                }

                float duration = GetWaterPourSeconds();
                if (secondsUsed < duration) return true;

                // Latch until RMB release to prevent auto-starting the next action.
                if (world.Side == EnumAppSide.Client) SetNeedsRelease(byPlayer);

                if (world.Side == EnumAppSide.Server)
                {
                    if (!TryApplyWaterPourServer(world, byPlayer, pos, be, activeSlot, plate))
                    {
                        ClearTimed(byPlayer);
                        return false;
                    }

                    world.PlaySoundAt(FizzSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, 1f);
                }

                ClearTimed(byPlayer);
                return false;
            }

            return false;
        }

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
                    if (IsTimed(byPlayer, pos, ActionDeveloper) && secondsUsed >= GetDeveloperPourSeconds())
                    {
                        SetNeedsRelease(byPlayer);
                    }
                    else if (IsTimed(byPlayer, pos, ActionFixer) && secondsUsed >= GetFixerPourSeconds())
                    {
                        SetNeedsRelease(byPlayer);
                    }
                    else if (IsTimed(byPlayer, pos, ActionWater) && secondsUsed >= GetWaterPourSeconds())
                    {
                        SetNeedsRelease(byPlayer);
                    }
                }
            }
            catch (Exception ex)
            {
                world?.Logger?.Warning("[Collodion] OnBlockInteractStop timed-release check failed: {0}", ex.Message);
            }

            // Clear any in-progress timed interaction for this player.
            ClearTimed(byPlayer);
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        private static bool IsReclaimablePlate(IWorldAccessor world, ItemStack? stack)
        {
            if (stack == null) return false;
            bool isAnyWetPlate = IsPlate(stack, SilveredPlateItemCode) || IsPlate(stack, ExposedPlateItemCode) || IsPlate(stack, DevelopedPlateItemCode);
            return isAnyWetPlate && WetPlateAttrs.IsDry(world, stack);
        }

        private static bool TryGetReclaimContext(BlockEntityDevelopmentTray be, IWorldAccessor world, out ItemStack plate)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null || !IsReclaimablePlate(world, plateStack))
            {
                plate = null!;
                return false;
            }
            plate = plateStack;
            return true;
        }

        private static float GetWaterPourSeconds() => 1.25f;

        private bool TryApplyDeveloperPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate, bool isExposed, int currentPours, DevelopmentParameters development)
        {
            if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, new AssetLocation(development.DeveloperPortionCode), development.DeveloperAmountPerPour))
            {
                Tell(byPlayer, "Wetplate: need developer (at least 1 portion).", pos);
                return false;
            }

            ItemStack newPlate = plate;
            if (isExposed)
            {
                Item? developedItem = world.GetItem(DevelopedPlateItemCode);
                if (developedItem == null) return false;

                newPlate = new ItemStack(developedItem);
                    try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); }
                    catch (Exception ex) { world?.Logger?.Warning("[Collodion] TryApplyDeveloperPourServer: attribute merge failed: {0}", ex.Message); }
            }

            int newPours = currentPours + 1;
            if (newPours > development.DeveloperPourCount) newPours = development.DeveloperPourCount;

            newPlate.Attributes.SetInt(WetPlateAttrs.DevelopPours, newPours);
            newPlate.Attributes.SetInt(WetPlateAttrs.DeveloperPourCountMax, development.DeveloperPourCount);
            PlateStateService.EnsureProcessId(newPlate);
            PlateStateService.SetStage(newPlate, newPours >= development.DeveloperPourCount ? PlateStage.Developed : PlateStage.Developing);

            double baseHours = WetPlateAttrs.ResolveWetDurationHours(api);
            WetPlateAttrs.ResetWetTimer(world!, newPlate, baseHours * development.WetDurationMultiplier);

            be.TrySetPlate(newPlate);
            SwapTrayBlockForPlateStage(world!, pos, PlateStageUtil.ToAttributeString(PlateStage.Developed), newPlate);
            return true;
        }

        private bool TryApplyFixerPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate, DevelopmentParameters development)
        {
            if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, new AssetLocation(development.FixerPortionCode), development.FixerAmountPerPour))
            {
                Tell(byPlayer, "Wetplate: need fixer (at least 1 portion).", pos);
                return false;
            }

            Item? finishedItem = world.GetItem(FinishedPlateItemCode);
            if (finishedItem == null) return false;

            ItemStack newPlate = new ItemStack(finishedItem);
            try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); }
            catch (Exception ex) { world?.Logger?.Warning("[Collodion] TryApplyFixerPourServer: attribute merge failed: {0}", ex.Message); }
            PlateStateService.EnsureProcessId(newPlate);
            PlateStateService.SetStage(newPlate, PlateStage.Finished);

            be.TrySetPlate(newPlate);
            SwapTrayBlockForPlateStage(world!, pos, PlateStageUtil.ToAttributeString(PlateStage.Finished), newPlate);
            return true;
        }

        private bool TryApplyWaterPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate)
        {
            if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, WaterPortionCode, GetChemicalUnitsPerUse()))
            {
                Tell(byPlayer, "Wetplate: need water (at least 1 portion).", pos);
                return false;
            }

            Item? roughGlassItem = world.GetItem(RoughGlassPlateItemCode);
            if (roughGlassItem == null) return false;

            ItemStack reclaimedPlate = new ItemStack(roughGlassItem);
            be.TrySetPlate(reclaimedPlate);
            SwapTrayBlockForPlateStage(world, pos, PlateStageUtil.ToAttributeString(PlateStage.Finished), reclaimedPlate);
            return true;
        }
    }
}
