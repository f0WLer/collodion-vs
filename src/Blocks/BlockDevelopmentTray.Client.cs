using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public sealed partial class BlockDevelopmentTray
    {
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
    }
}
