using Photochemistry.AdminTooling;
using Photochemistry.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Photochemistry.Tray
{
    public sealed partial class BlockDevelopmentTray
    {
        private static readonly AssetLocation _chemicalPourSound = new("game:sounds/effect/water-fill");

        private enum TrayStartKind { None, TakePlate, InsertPlate, ChemicalPour }

        // Tray interaction starts are intentionally limited to three cases:
        // 1. take plate out
        // 2. insert plate
        // 3. start a chemical pour
        // The case, chemical kind, and insert stage are resolved once here so neither
        // side independently re-derives what product-level action is taking place.
        private bool HandleTrayObjectInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Prevent immediately starting another timed action while RMB is still held.
            // This is enforced client-side only (server cannot observe mouse button state).
            if (world.Side == EnumAppSide.Client && TrayTimedInteractionState.NeedsRelease(byPlayer)) return false;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityDevelopmentTray be)
                return false;

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;

            TrayStartKind kind = ResolveTrayStartKind(world, be, held, activeSlot);
            if (kind == TrayStartKind.None) return false;

            TrayActionKind chemKind = kind == TrayStartKind.ChemicalPour
                ? ResolveChemicalKind(activeSlot)
                : default;

            PlateStage insertStage = kind == TrayStartKind.InsertPlate
                ? ResolveInsertStage(world, held!)
                : default;

            if (world.Side == EnumAppSide.Client)
                return HandleInteractStartClient(world, byPlayer, blockSel, be, activeSlot, kind, chemKind);

            return HandleInteractStartServer(world, byPlayer, blockSel, be, activeSlot, held, kind, chemKind, insertStage);
        }

        private TrayStartKind ResolveTrayStartKind(IWorldAccessor world, BlockEntityDevelopmentTray be, ItemStack? held, ItemSlot? activeSlot)
        {
            if (held == null)
                return be.HasPlate ? TrayStartKind.TakePlate : TrayStartKind.None;

            if (!be.HasPlate && (IsInsertablePlate(held) || IsDrySensitizedForReclaim(world, held)))
                return TrayStartKind.InsertPlate;

            if (IsHoldingChemical(activeSlot, _developerPortionCode)
                || IsHoldingChemical(activeSlot, _fixerPortionCode)
                || IsHoldingChemical(activeSlot, _waterPortionCode))
                return TrayStartKind.ChemicalPour;

            return TrayStartKind.None;
        }

        private TrayActionKind ResolveChemicalKind(ItemSlot? activeSlot)
        {
            if (IsHoldingChemical(activeSlot, _developerPortionCode)) return TrayActionKind.Developer;
            if (IsHoldingChemical(activeSlot, _fixerPortionCode)) return TrayActionKind.Fixer;
            return TrayActionKind.Water;
        }

        private static PlateStage ResolveInsertStage(IWorldAccessor world, ItemStack held)
        {
            if (IsDrySensitizedForReclaim(world, held)) return PlateStage.Exposed;
            return IsDevelopingStage(held) ? PlateStage.Developed : PlateStage.Exposed;
        }

        // Receives the already-resolved kind so it never independently re-derives the product-level action.
        // Does not mutate tray contents or authoritatively consume items.
        private bool HandleInteractStartClient(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, TrayStartKind kind, TrayActionKind chemKind)
        {
            switch (kind)
            {
                case TrayStartKind.TakePlate:
                    return true;

                case TrayStartKind.InsertPlate:
                    return true;

                case TrayStartKind.ChemicalPour:
                    return HandleChemicalPourStartClient(world, byPlayer, blockSel, be, activeSlot, chemKind);

                default:
                    return false;
            }
        }

        private bool HandleChemicalPourStartClient(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, TrayActionKind chemKind)
        {
            switch (chemKind)
            {
                case TrayActionKind.Developer:
                {
                    if (!TryGetDeveloperPourContext(be, out ItemStack clientDevPlate, out bool clientIsExposed, out int currentPours)) return false;
                    if (currentPours >= RequiredDeveloperPours) return false;
                    if (PlateDryingTransition.IsDry(world, clientDevPlate))
                    {
                        (world.Api as ICoreClientAPI)?.ShowChatMessage(Lang.Get("photochemistry:msg-tray-plate-dried"));
                        return false;
                    }
                    if (!PlateChemicalUtil.HasConsumableChemical(activeSlot, _developerPortionCode, GetChemicalUnitsPerUse())) return false;

                    // Seal the partial exposure before the first developer pour transitions it away from ExposurePaused.
                    // Must happen client-side at RMB-press time: tick ordering means the server transitions the stage
                    // (ExposurePaused → Developing) before the client's step handler runs, so completion-side logic
                    // would never see the ExposurePaused stage.
                    // TrySendSealForTray is idempotent: SealToPng deletes the .pex file on success.
                    if (clientIsExposed && PlateAttributes.GetStage(clientDevPlate) == PlateStage.ExposurePaused)
                        if (world.Api is ICoreClientAPI capiSeal)
                        {
                            // Develop whitelist: sealing is the act that creates server data. If this client
                            // isn't allowed, refuse the pour now so the exposure (.pex) is kept rather than
                            // sealed-then-rejected server-side. The server gate is the real enforcement.
                            if (!(PhotochemistryConfigAccess.ResolveClientModSystem(capiSeal)?.AdminToolingBridge.ClientDevelopAllowed ?? true))
                            {
                                capiSeal.ShowChatMessage(Lang.Get("photochemistry:msg-develop-not-whitelisted"));
                                return false;
                            }
                            PhotochemistryConfigAccess.ResolveModSystem(capiSeal)?.FieldCameraBridge.TrySendSealForTray(capiSeal, blockSel.Position, clientDevPlate);
                        }

                    TrayTimedInteractionState.Begin(byPlayer, blockSel.Position, ActionDeveloper, GetDeveloperPourSeconds());
                    return true;
                }

                case TrayActionKind.Fixer:
                {
                    if (!TryGetFixerPourContext(be, out ItemStack clientFixPlate, out _)) return false;
                    if (PlateDryingTransition.IsDry(world, clientFixPlate))
                    {
                        (world.Api as ICoreClientAPI)?.ShowChatMessage(Lang.Get("photochemistry:msg-tray-plate-dried"));
                        return false;
                    }
                    if (!PlateChemicalUtil.HasConsumableChemical(activeSlot, _fixerPortionCode, GetChemicalUnitsPerUse())) return false;

                    TrayTimedInteractionState.Begin(byPlayer, blockSel.Position, ActionFixer, GetFixerPourSeconds());
                    return true;
                }

                case TrayActionKind.Water:
                {
                    if (!TryGetReclaimContext(be, world, out _)) return false;
                    if (!PlateChemicalUtil.HasConsumableChemical(activeSlot, _waterPortionCode, GetChemicalUnitsPerUse())) return false;

                    TrayTimedInteractionState.Begin(byPlayer, blockSel.Position, ActionWater, GetWaterPourSeconds());
                    return true;
                }

                default:
                    return false;
            }
        }

        // Receives the already-resolved kind so it never independently re-derives the product-level action.
        private bool HandleInteractStartServer(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack? held, TrayStartKind kind, TrayActionKind chemKind, PlateStage insertStage)
        {
            BlockPos trayPos = blockSel.Position;

            switch (kind)
            {
                case TrayStartKind.TakePlate:
                {
                    ItemStack? taken = be.TakePlate();
                    if (taken == null) return false;

                    SwapTrayBlockForPlateStage(world, trayPos, null, null);
                    GiveOrDrop(world, byPlayer, taken, trayPos);
                    return true;
                }

                case TrayStartKind.InsertPlate:
                {
                    if (activeSlot == null || held == null) return false;
                    return TryInsertHeldPlateIntoTray(world, byPlayer, trayPos, be, activeSlot, held, insertStage);
                }

                case TrayStartKind.ChemicalPour:
                    return TryStartTimedChemicalActionServer(world, byPlayer, trayPos, be, activeSlot, chemKind);

                default:
                    return false;
            }
        }

        private bool TryInsertHeldPlateIntoTray(IWorldAccessor world, IPlayer byPlayer, BlockPos trayPos, BlockEntityDevelopmentTray be, ItemSlot activeSlot, ItemStack held, PlateStage trayStage)
        {
            // Ensure tray photo orientation always tracks the player who is actively using the tray.
            // This acts as a reliable fallback if placement-time facing capture is unavailable.
            BlockFacing insertFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.Pos?.Yaw ?? 0f);
            be.SetPlacementFacing(insertFacing.Code, markBlockDirty: false);

            ItemStack toInsert = held.Clone();
            toInsert.StackSize = 1;

            if (!be.TryInsertPlate(toInsert)) return false;

            PlateAttributes.EnsureStage(toInsert, trayStage);
            SwapTrayBlockForPlateStage(world, trayPos, PlateAttributes.ToAttributeString(trayStage), toInsert);

            activeSlot.TakeOut(1);
            activeSlot.MarkDirty();
            return true;
        }

        private bool TryValidateStartForAction(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, TrayActionKind actionKind)
        {
            switch (actionKind)
            {
                case TrayActionKind.Developer:
                    if (!TryGetDeveloperPourContext(be, out ItemStack devPlate, out _, out int currentPours)) return false;
                    if (currentPours >= RequiredDeveloperPours) return false;
                    if (PlateDryingTransition.IsDry(world, devPlate))
                    {
                        Tell(byPlayer, Lang.Get("photochemistry:msg-tray-plate-dried"), pos);
                        return false;
                    }
                    string? devPhotographer = devPlate.Attributes?.GetString(PlateAttributes.PhotographerUid);
                    if (!string.IsNullOrEmpty(devPhotographer) && !string.Equals(devPhotographer, byPlayer.PlayerUID, StringComparison.OrdinalIgnoreCase))
                    {
                        Tell(byPlayer, Lang.Get("photochemistry:msg-tray-other-photographer"), pos);
                        return false;
                    }
                    break;
                case TrayActionKind.Fixer:
                    if (!TryGetFixerPourContext(be, out ItemStack fixPlate, out int pours)) return false;
                    if (PlateDryingTransition.IsDry(world, fixPlate))
                    {
                        Tell(byPlayer, Lang.Get("photochemistry:msg-tray-plate-dried"), pos);
                        return false;
                    }

                    if (pours < RequiredDeveloperPours)
                    {
                        Tell(byPlayer, Lang.Get("photochemistry:msg-tray-underdeveloped", pours, RequiredDeveloperPours), pos);
                        return false;
                    }

                    string? fixPhotographer = fixPlate.Attributes?.GetString(PlateAttributes.PhotographerUid);
                    if (!string.IsNullOrEmpty(fixPhotographer) && !string.Equals(fixPhotographer, byPlayer.PlayerUID, StringComparison.OrdinalIgnoreCase))
                    {
                        Tell(byPlayer, Lang.Get("photochemistry:msg-tray-other-photographer"), pos);
                        return false;
                    }
                    break;
                default:
                    if (!TryGetReclaimContext(be, world, out _)) return false;
                    break;
            }

            AssetLocation portionCode = GetPortionCode(actionKind);
            int amountPerUse = GetChemicalUnitsPerUse();
            if (!PlateChemicalUtil.HasConsumableChemical(activeSlot, portionCode, amountPerUse))
            {
                Tell(byPlayer, GetMissingChemicalMessage(actionKind), pos);
                return false;
            }

            return true;
        }

        private bool TryStartTimedChemicalActionServer(IWorldAccessor world, IPlayer byPlayer, BlockPos trayPos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, TrayActionKind chemKind)
        {
            if (!TryValidateStartForAction(world, byPlayer, trayPos, be, activeSlot, chemKind)) return false;

            world.PlaySoundAt(_chemicalPourSound, trayPos.X + 0.5, trayPos.Y + 0.5, trayPos.Z + 0.5, null);

            switch (chemKind)
            {
                case TrayActionKind.Developer:
                    TrayTimedInteractionState.Begin(byPlayer, trayPos, ActionDeveloper, GetDeveloperPourSeconds());
                    return true;

                case TrayActionKind.Fixer:
                    TrayTimedInteractionState.Begin(byPlayer, trayPos, ActionFixer, GetFixerPourSeconds());
                    return true;

                case TrayActionKind.Water:
                    TrayTimedInteractionState.Begin(byPlayer, trayPos, ActionWater, GetWaterPourSeconds());
                    return true;

                default:
                    return false;
            }
        }

        private static bool IsDrySensitizedForReclaim(IWorldAccessor world, ItemStack? stack)
        {
            if (stack == null || !PlateDryingTransition.IsDry(world, stack)) return false;

            return PlateAttributes.GetStage(stack) == PlateStage.Sensitized;
        }

        // Allows exposed, paused-exposure, developing, and developed plates to enter the tray workflow.
        private static bool IsInsertablePlate(ItemStack? stack)
        {
            if (stack == null) return false;

            PlateStage stage = PlateAttributes.GetStage(stack);
            return stage == PlateStage.Exposed
                || stage == PlateStage.ExposurePaused
                || IsDevelopingStage(stack);
        }

        private static bool IsDevelopingStage(ItemStack? stack)
        {
            if (stack == null) return false;

            PlateStage stage = PlateAttributes.GetStage(stack);
            return stage == PlateStage.Developing || stage == PlateStage.Developed;
        }
    }
}