using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion
{
    public sealed class BlockDevelopmentTray : Block
    {
        // Liquid portion itemsPerLitre is 100 in our json.
        private const int DefaultChemicalUnitsPerUse = 40;

        internal const string TimedAttrKey = "collodionDevTrayTimed";
        internal const string TimedNeedReleaseKey = "collodionDevTrayNeedRelease";
        internal const string TimedActionKey = "action";
        internal const string TimedXKey = "x";
        internal const string TimedYKey = "y";
        internal const string TimedZKey = "z";
        internal const string TimedStartMsKey = "startMs";
        internal const string TimedDurationMsKey = "durationMs";

        internal const string ActionDeveloper = "developer";
        internal const string ActionFixer = "fixer";
        internal const string ActionWater = "water";

        private CollodionConfig? Cfg => api.ModLoader.GetModSystem<CollodionModSystem>()?.Config;

        private static readonly AssetLocation SilveredPlateItemCode = new AssetLocation("collodion", "silveredplate");
        private static readonly AssetLocation ExposedPlateItemCode = new AssetLocation("collodion", "exposedplate");
        private static readonly AssetLocation DevelopedPlateItemCode = new AssetLocation("collodion", "developedplate");
        private static readonly AssetLocation FinishedPlateItemCode = new AssetLocation("collodion", "finishedphotoplate");

        private static readonly AssetLocation DeveloperPortionCode = new AssetLocation("collodion", "developerportion");
        private static readonly AssetLocation FixerPortionCode = new AssetLocation("collodion", "fixerportion");
        private static readonly AssetLocation WaterPortionCode = new AssetLocation("game", "waterportion");
        private static readonly AssetLocation RoughGlassPlateItemCode = new AssetLocation("collodion", "roughglassplate");
        private static readonly AssetLocation ChemicalPourSound = new AssetLocation("game:sounds/effect/water-fill");
        private static readonly AssetLocation FizzSound = new AssetLocation("collodion", "sounds/fizz");

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            bool placed = base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
            if (!placed) return false;

            if (world == null || blockSel?.Position == null) return true;

            BlockPos placedPos = ResolvePlacedPos(world, blockSel);
            if (world.BlockAccessor.GetBlockEntity(placedPos) is BlockEntityDevelopmentTray be)
            {
                BlockFacing playerFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
                be.SetPlacementFacing(playerFacing.Code, markBlockDirty: true);
            }

            return true;
        }

        private float GetDeveloperPourSeconds()
        {
            float seconds = Cfg?.DevelopmentTrayInteractions?.Developer?.DurationSeconds ?? 1.25f;
            return seconds < 0.05f ? 0.05f : seconds;
        }

        private float GetFixerPourSeconds()
        {
            float seconds = Cfg?.DevelopmentTrayInteractions?.Fixer?.DurationSeconds ?? 1.25f;
            return seconds < 0.05f ? 0.05f : seconds;
        }

        private int GetChemicalUnitsPerUse()
        {
            int amount = Cfg?.PlateProcessing?.DevelopmentTrayChemicalUnitsPerUse ?? DefaultChemicalUnitsPerUse;
            if (amount < 1) amount = 1;
            return amount;
        }

        private static void BeginTimed(IPlayer byPlayer, BlockPos pos, string action, float durationSeconds)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return;

            ITreeAttribute tree = byPlayer.Entity.Attributes.GetOrAddTreeAttribute(TimedAttrKey);
            tree.SetString(TimedActionKey, action);
            tree.SetInt(TimedXKey, pos.X);
            tree.SetInt(TimedYKey, pos.Y);
            tree.SetInt(TimedZKey, pos.Z);

            long nowMs = 0;
            try
            {
                nowMs = byPlayer.Entity?.World?.ElapsedMilliseconds ?? 0;
            }
            catch
            {
                nowMs = 0;
            }

            if (nowMs <= 0) nowMs = Environment.TickCount64;
            tree.SetLong(TimedStartMsKey, nowMs);

            if (durationSeconds > 0f)
            {
                int durationMs = (int)Math.Round(durationSeconds * 1000f);
                if (durationMs < 1) durationMs = 1;
                tree.SetInt(TimedDurationMsKey, durationMs);
            }
        }

        private static bool IsTimed(IPlayer byPlayer, BlockPos pos, string action)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return false;
            ITreeAttribute? tree = byPlayer.Entity.Attributes.GetTreeAttribute(TimedAttrKey);
            if (tree == null) return false;
            if (!tree.GetString(TimedActionKey, "").Equals(action, System.StringComparison.Ordinal)) return false;
            return tree.GetInt(TimedXKey) == pos.X && tree.GetInt(TimedYKey) == pos.Y && tree.GetInt(TimedZKey) == pos.Z;
        }

        private static void ClearTimed(IPlayer byPlayer)
        {
            if (byPlayer?.Entity?.Attributes == null) return;
            byPlayer.Entity.Attributes.RemoveAttribute(TimedAttrKey);
        }

        private static bool NeedsRelease(IPlayer byPlayer)
        {
            try
            {
                return byPlayer?.Entity?.Attributes?.GetInt(TimedNeedReleaseKey, 0) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SetNeedsRelease(IPlayer byPlayer)
        {
            try
            {
                byPlayer?.Entity?.Attributes?.SetInt(TimedNeedReleaseKey, 1);
            }
            catch { }
        }

        private static bool IsHoldingChemical(ItemSlot? slot, AssetLocation code)
        {
            return slot?.Itemstack != null && WetPlateChemicalUtil.IsChemicalOrContainerWith(slot.Itemstack, code);
        }

        private static int GetDevelopPours(ItemStack plate, int defaultValue)
        {
            int pours;
            try
            {
                pours = plate.Attributes.GetInt(WetPlateAttrs.DevelopPours, defaultValue);
            }
            catch
            {
                pours = defaultValue;
            }

            return pours;
        }

        private static bool TryGetDeveloperPourContext(BlockEntityDevelopmentTray be, out ItemStack plate, out bool isExposed, out bool isDeveloped, out int currentPours)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null)
            {
                plate = null!;
                isExposed = false;
                isDeveloped = false;
                currentPours = 0;
                return false;
            }

            plate = plateStack;

            isExposed = IsPlate(plate, ExposedPlateItemCode);
            isDeveloped = IsPlate(plate, DevelopedPlateItemCode);
            if (!isExposed && !isDeveloped)
            {
                currentPours = 0;
                return false;
            }

            currentPours = GetDevelopPours(plate, isDeveloped ? WetPlateChemicalUtil.DevelopPoursRequired : 0);
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

            if (!IsPlate(plate, DevelopedPlateItemCode))
            {
                pours = 0;
                return false;
            }

            pours = GetDevelopPours(plate, WetPlateChemicalUtil.DevelopPoursRequired);
            return true;
        }

        private bool TryApplyDeveloperPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate, bool isExposed, int currentPours)
        {
            if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, DeveloperPortionCode, GetChemicalUnitsPerUse()))
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
                try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); } catch { }
            }

            int newPours = currentPours + 1;
            if (newPours > WetPlateChemicalUtil.DevelopPoursRequired) newPours = WetPlateChemicalUtil.DevelopPoursRequired;

            newPlate.Attributes.SetInt(WetPlateAttrs.DevelopPours, newPours);
            newPlate.Attributes.SetString(WetPlateAttrs.PlateStage, newPours >= WetPlateChemicalUtil.DevelopPoursRequired ? "developed" : "developing");
            WetPlateAttrs.ResetWetTimer(world, newPlate, Cfg?.PlateProcessing?.WetPlateDurationHours ?? WetPlateAttrs.DefaultWetDurationHours);

            be.TrySetPlate(newPlate);
            SwapTrayBlockForPlateStage(world, pos, "developed", newPlate);
            return true;
        }

        private bool TryApplyFixerPourServer(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate)
        {
            if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, FixerPortionCode, GetChemicalUnitsPerUse()))
            {
                Tell(byPlayer, "Wetplate: need fixer (at least 1 portion).", pos);
                return false;
            }

            Item? finishedItem = world.GetItem(FinishedPlateItemCode);
            if (finishedItem == null) return false;

            ItemStack newPlate = new ItemStack(finishedItem);
            try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); } catch { }
            newPlate.Attributes.SetString(WetPlateAttrs.PlateStage, "finished");

            be.TrySetPlate(newPlate);
            SwapTrayBlockForPlateStage(world, pos, "finished", newPlate);
            return true;
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
            SwapTrayBlockForPlateStage(world, pos, "finished", reclaimedPlate);
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;

            // Prevent immediately starting another timed action while RMB is still held.
            // This is enforced client-side only (server cannot observe mouse button state).
            if (world.Side == EnumAppSide.Client && NeedsRelease(byPlayer)) return false;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityDevelopmentTray be)
            {
                return false;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            int chemicalUnitsPerUse = GetChemicalUnitsPerUse();

            if (world.Side == EnumAppSide.Client)
                return HandleInteractStartClient(world, byPlayer, blockSel, be, activeSlot, held, chemicalUnitsPerUse);

            return HandleInteractStartServer(world, byPlayer, blockSel, be, activeSlot, held, chemicalUnitsPerUse);
        }

        private bool HandleInteractStartClient(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack? held, int chemicalUnitsPerUse)
        {
            // Empty hand: take plate out.
            if (held == null)
            {
                return be.HasPlate;
            }

            // Holding a plate: insert (only if tray empty). Dry plates still insertable for water rinse.
            if (IsInsertablePlate(held))
            {
                return !be.HasPlate;
            }

            // Holding a dry silvered plate: insert for water rinse.
            if (IsPlate(held, SilveredPlateItemCode) && WetPlateAttrs.IsDry(world, held))
            {
                return !be.HasPlate;
            }

            // Holding developer: can attempt timed pour when tray has an exposed/developed plate.
            if (IsHoldingChemical(activeSlot, DeveloperPortionCode))
            {
                if (!TryGetDeveloperPourContext(be, out ItemStack clientDevPlate, out _, out _, out int currentPours)) return false;

                if (currentPours >= WetPlateChemicalUtil.DevelopPoursRequired) return false;
                if (WetPlateAttrs.IsDry(world, clientDevPlate))
                {
                    (world.Api as ICoreClientAPI)?.ShowChatMessage("Wetplate: the plate has dried and can no longer be used.");
                    return false;
                }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, DeveloperPortionCode, chemicalUnitsPerUse)) return false;

                // Prime local timed state so client-only visuals can react immediately.
                BeginTimed(byPlayer, blockSel.Position, ActionDeveloper, GetDeveloperPourSeconds());
                return true;
            }

            // Holding fixer: allow attempt when there's a developed plate (server will message if not ready).
            if (IsHoldingChemical(activeSlot, FixerPortionCode))
            {
                if (!TryGetFixerPourContext(be, out ItemStack clientFixPlate, out _)) return false;
                if (WetPlateAttrs.IsDry(world, clientFixPlate))
                {
                    (world.Api as ICoreClientAPI)?.ShowChatMessage("Wetplate: the plate has dried and can no longer be used.");
                    return false;
                }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, FixerPortionCode, chemicalUnitsPerUse)) return false;

                // Prime local timed state so client-only visuals can react immediately.
                BeginTimed(byPlayer, blockSel.Position, ActionFixer, GetFixerPourSeconds());
                return true;
            }

            // Holding water: can attempt rinse when tray has a dry plate.
            if (IsHoldingChemical(activeSlot, WaterPortionCode))
            {
                if (!TryGetReclaimContext(be, world, out _)) return false;
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, WaterPortionCode, chemicalUnitsPerUse)) return false;

                BeginTimed(byPlayer, blockSel.Position, ActionWater, GetWaterPourSeconds());
                return true;
            }

            return false;
        }

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

                string stage = IsPlate(toInsert, ExposedPlateItemCode) ? "exposed" : "developed";
                SwapTrayBlockForPlateStage(world, blockSel.Position, stage, toInsert);

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

                SwapTrayBlockForPlateStage(world, blockSel.Position, "exposed", toInsert);

                activeSlot.TakeOut(1);
                activeSlot.MarkDirty();
                return true;
            }

            // Holding developer: start timed develop pour.
            if (IsHoldingChemical(activeSlot, DeveloperPortionCode))
            {
                if (!TryGetDeveloperPourContext(be, out ItemStack devPlate, out _, out _, out int currentPours)) return false;

                if (currentPours >= WetPlateChemicalUtil.DevelopPoursRequired) return false;
                if (WetPlateAttrs.IsDry(world, devPlate))
                {
                    Tell(byPlayer, "Wetplate: the plate has dried and can no longer be used.", blockSel.Position);
                    return false;
                }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, DeveloperPortionCode, chemicalUnitsPerUse))
                {
                    Tell(byPlayer, "Wetplate: need developer (at least 1 portion).", blockSel.Position);
                    return false;
                }

                world.PlaySoundAt(ChemicalPourSound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                BeginTimed(byPlayer, blockSel.Position, ActionDeveloper, GetDeveloperPourSeconds());
                return true;
            }

            // Holding fixer: start timed fix pour.
            if (IsHoldingChemical(activeSlot, FixerPortionCode))
            {
                if (!TryGetFixerPourContext(be, out ItemStack fixPlate, out int pours)) return false;

                if (WetPlateAttrs.IsDry(world, fixPlate))
                {
                    Tell(byPlayer, "Wetplate: the plate has dried and can no longer be used.", blockSel.Position);
                    return false;
                }
                if (pours < WetPlateChemicalUtil.DevelopPoursRequired)
                {
                    Tell(byPlayer, $"Wetplate: plate not fully developed ({pours}/{WetPlateChemicalUtil.DevelopPoursRequired}).", blockSel.Position);
                    return false;
                }

                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, FixerPortionCode, chemicalUnitsPerUse))
                {
                    Tell(byPlayer, "Wetplate: need fixer (at least 1 portion).", blockSel.Position);
                    return false;
                }

                world.PlaySoundAt(ChemicalPourSound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                BeginTimed(byPlayer, blockSel.Position, ActionFixer, GetFixerPourSeconds());
                return true;
            }

            // Holding water: rinse a dry plate to reclaim rough glass.
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

                ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                if (!IsHoldingChemical(activeSlot, DeveloperPortionCode)) { ClearTimed(byPlayer); return false; }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, DeveloperPortionCode, chemicalUnitsPerUse))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, "Wetplate: need developer (at least 1 portion).", pos);
                    }
                    ClearTimed(byPlayer);
                    return false;
                }

                if (!TryGetDeveloperPourContext(be, out ItemStack plate, out bool isExposed, out bool isDeveloped, out int currentPours))
                {
                    ClearTimed(byPlayer);
                    return false;
                }

                if (currentPours >= WetPlateChemicalUtil.DevelopPoursRequired) { ClearTimed(byPlayer); return false; }

                float duration = GetDeveloperPourSeconds();
                if (secondsUsed < duration) return true;

                // Latch until RMB release to prevent auto-starting the next pour.
                if (world.Side == EnumAppSide.Client) SetNeedsRelease(byPlayer);

                if (world.Side == EnumAppSide.Server)
                {
                    if (!TryApplyDeveloperPourServer(world, byPlayer, pos, be, activeSlot, plate, isExposed, currentPours))
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

                ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                if (!IsHoldingChemical(activeSlot, FixerPortionCode)) { ClearTimed(byPlayer); return false; }
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, FixerPortionCode, chemicalUnitsPerUse))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, "Wetplate: need fixer (at least 1 portion).", pos);
                    }
                    ClearTimed(byPlayer);
                    return false;
                }

                if (!TryGetFixerPourContext(be, out ItemStack plate, out int pours))
                {
                    ClearTimed(byPlayer);
                    return false;
                }

                if (pours < WetPlateChemicalUtil.DevelopPoursRequired)
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, $"Wetplate: plate not fully developed ({pours}/{WetPlateChemicalUtil.DevelopPoursRequired}).", pos);
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
                    if (!TryApplyFixerPourServer(world, byPlayer, pos, be, activeSlot, plate))
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
            catch { }

            // Clear any in-progress timed interaction for this player.
            ClearTimed(byPlayer);
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // Always drop the base tray block (red/blue/fire), not a loaded variant.
            var drops = new List<ItemStack>();

            Block? baseTray = GetBaseTrayBlock(world);
            if (baseTray != null)
            {
                drops.Add(new ItemStack(baseTray));
            }
            else
            {
                drops.AddRange(base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? Array.Empty<ItemStack>());
            }

            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityDevelopmentTray be && be.PlateStack != null)
            {
                drops.Add(be.PlateStack.Clone());
            }

            return drops.ToArray();
        }

        private Block? GetBaseTrayBlock(IWorldAccessor world)
        {
            if (world == null || Code == null) return null;

            string path = Code.Path;
            if (!path.StartsWith("developmenttray-")) return null;

            // path is one of:
            // developmenttray-red
            // developmenttray-red-exposed/developed/finished
            string rest = path.Substring("developmenttray-".Length);
            int dash = rest.IndexOf('-');
            string clay = dash >= 0 ? rest.Substring(0, dash) : rest;

            AssetLocation baseLoc = new AssetLocation(Code.Domain, $"developmenttray-{clay}");
            return world.GetBlock(baseLoc);
        }

        private void SwapTrayBlockForPlateStage(IWorldAccessor world, BlockPos pos, string? stage, ItemStack? plateToKeep)
        {
            if (world == null || pos == null || Code == null) return;

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
            if (target == null) return;

            int targetId = target.Id;
            if (targetId <= 0) return;

            world.BlockAccessor.SetBlock(targetId, pos);

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

        private BlockPos ResolvePlacedPos(IWorldAccessor world, BlockSelection blockSel)
        {
            BlockPos selectedPos = blockSel.Position;
            Block selectedBlock = world.BlockAccessor.GetBlock(selectedPos);
            if (selectedBlock != null && selectedBlock.IsReplacableBy(this))
            {
                return selectedPos;
            }

            return selectedPos.AddCopy(blockSel.Face);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            if (world == null || selection == null) return System.Array.Empty<WorldInteraction>();

            var interactions = new List<WorldInteraction>();

            BlockPos pos = selection.Position;
            if (pos == null) return System.Array.Empty<WorldInteraction>();

            BlockEntityDevelopmentTray? be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityDevelopmentTray;
            ItemStack? plate = be?.PlateStack;

            // Insert plate (exposed or developed).
            if (plate == null)
            {
                var exposedItem = world.GetItem(ExposedPlateItemCode);
                var developedItem = world.GetItem(DevelopedPlateItemCode);

                var stacks = new List<ItemStack>();
                if (exposedItem != null) stacks.Add(new ItemStack(exposedItem));
                if (developedItem != null) stacks.Add(new ItemStack(developedItem));

                if (stacks.Count > 0)
                {
                    interactions.Add(new WorldInteraction
                    {
                        ActionLangCode = "collodion:heldhelp-developmenttray-insertplate",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = stacks.ToArray()
                    });
                }
            }
            else
            {
                // Take plate.
                interactions.Add(new WorldInteraction
                {
                    ActionLangCode = "collodion:heldhelp-developmenttray-takeplate",
                    MouseButton = EnumMouseButton.Right
                });

                bool canDevelop = false;
                bool canFix = false;

                if (be != null)
                {
                    // Drive held-help by actual development progress, not just item code.
                    if (TryGetDeveloperPourContext(be, out _, out _, out _, out int currentPours))
                    {
                        canDevelop = currentPours < WetPlateChemicalUtil.DevelopPoursRequired;
                    }

                    if (TryGetFixerPourContext(be, out _, out int pours))
                    {
                        canFix = pours >= WetPlateChemicalUtil.DevelopPoursRequired;
                    }
                }
                else
                {
                    // Fallback when BE context is unavailable.
                    canDevelop = IsPlate(plate, ExposedPlateItemCode);
                    canFix = IsPlate(plate, DevelopedPlateItemCode);
                }

                // Develop / continue developing.
                if (canDevelop)
                {
                    Item? developer = world.GetItem(DeveloperPortionCode);
                    if (developer != null)
                    {
                        interactions.Add(new WorldInteraction
                        {
                            ActionLangCode = "collodion:heldhelp-developmenttray-develop",
                            MouseButton = EnumMouseButton.Right,
                            Itemstacks = new[] { new ItemStack(developer, GetChemicalUnitsPerUse()) }
                        });
                    }
                }

                // Fix.
                if (canFix)
                {
                    Item? fixer = world.GetItem(FixerPortionCode);
                    if (fixer != null)
                    {
                        interactions.Add(new WorldInteraction
                        {
                            ActionLangCode = "collodion:heldhelp-developmenttray-fix",
                            MouseButton = EnumMouseButton.Right,
                            Itemstacks = new[] { new ItemStack(fixer, GetChemicalUnitsPerUse()) }
                        });
                    }
                }
            }

            return interactions.ToArray();
        }

        private static void Tell(IPlayer byPlayer, string message, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp)
            {
                sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        private static bool IsInsertablePlate(ItemStack? stack)
        {
            return IsPlate(stack, ExposedPlateItemCode) || IsPlate(stack, DevelopedPlateItemCode);
        }

        private static bool IsPlate(ItemStack? stack, AssetLocation code)
        {
            return stack?.Collectible?.Code != null && stack.Collectible.Code == code;
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
    }
}
