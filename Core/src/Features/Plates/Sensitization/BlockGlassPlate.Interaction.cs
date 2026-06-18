using Collodion.AdminTooling;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion.Plates.Blocks
{
    public sealed partial class BlockGlassPlate
    {
        private static readonly AssetLocation _plainClothCode = new("game", "cloth-plain");
        private static readonly AssetLocation _collodionPortionCode      = new("photochemistry", "collodionportion");
        private static readonly AssetLocation _silverSolutionPortionCode = new("photochemistry", "silversolutionportion");

        private static readonly AssetLocation _polishSound = new("game:sounds/player/chalkdraw");
        private static readonly AssetLocation _collodionPourSound = new("game:sounds/effect/water-fill");
        private const int SensitizationChemicalAmount = 40;

        private static readonly AssetLocation _sensitizedPlateItemCode = new("photochemistry", "sensitizedplate");

        private static bool TryGetChemicalForState(string state, out AssetLocation chemicalCode)
        {
            if (state == "clean") { chemicalCode = _collodionPortionCode; return true; }
            if (state == "coated") { chemicalCode = _silverSolutionPortionCode; return true; }
            chemicalCode = default!;
            return false;
        }

        private bool HandleInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            string state = GetPlateState();
            bool isRough = state == "rough";
            bool isSensitizable = state == "clean" || state == "coated";

            bool isPolish = isRough && IsHoldingPlainCloth(byPlayer);
            bool isSensitize = isSensitizable
                && TryGetChemicalForState(state, out AssetLocation sensitizeChemical)
                && CanApplySensitizationChemical(byPlayer, sensitizeChemical);
            bool isPickup = IsEmptyHand(byPlayer);


            // Client: return true to show the "hold to interact" prompt without performing any state changes.
            if (world.Side == EnumAppSide.Client)
            {
                return isPolish || isSensitize || isPickup;
            }

            ItemStack? heldStack = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            ServerDebugLog.Notify(world.Api, "plate-interact: block-start state={0} polish={1} sensitize={2} pickup={3} held={4}x{5}",
                state, isPolish, isSensitize, isPickup,
                heldStack?.Collectible?.Code?.ToString() ?? "null", heldStack?.StackSize ?? 0);

            // Empty-hand pickup should always win so coated plates can be recovered at any point.
            if (isPickup)
            {
                GiveItemAndRemoveBlock(world, byPlayer, blockSel.Position);
                return true;
            }

            if (isPolish || isSensitize)
            {
                if (world.Side == EnumAppSide.Server)
                {
                    AssetLocation? sound = isPolish ? _polishSound : _collodionPourSound;
                    if (sound != null)
                    {
                        world.PlaySoundAt(sound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                    }
                }

                return true;
            }

            return false;
        }

        private bool HandlePourInteractionStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos, string state)
        {
            if (!TryGetChemicalForState(state, out AssetLocation chemicalCode)) return false;
            if (!CanApplySensitizationChemical(byPlayer, chemicalCode)) return false;

            if (secondsUsed < GetSensitizationPourSeconds()) return true;

            if (world.Side == EnumAppSide.Server)
            {
                if (byPlayer is not IServerPlayer sp) return false;
                ItemSlot? chemicalSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                TryAdvanceSensitizationStep(world, sp, pos, state, chemicalSlot!);
            }

            return false;
        }

        private static bool CanApplySensitizationChemical(IPlayer player, AssetLocation chemicalCode)
        {
            ItemSlot? slot = player.InventoryManager?.ActiveHotbarSlot;
            return PlateChemicalUtil.HasConsumableChemical(slot, chemicalCode, SensitizationChemicalAmount);
        }

        private bool TryAdvanceSensitizationStep(IWorldAccessor world, IServerPlayer player, BlockPos pos, string state, ItemSlot chemicalSlot)
        {
            if (!TryGetChemicalForState(state, out AssetLocation chemicalCode)) return false;

            bool isCreative = player.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (!isCreative && !PlateChemicalUtil.TryConsumeChemical(chemicalSlot, chemicalCode, SensitizationChemicalAmount))
            {
                ServerDebugLog.Notify(world.Api, "plate-interact: pour-complete state={0} chemical={1} → declined: could not consume {2} units from hand", state, chemicalCode, SensitizationChemicalAmount);
                return false;
            }

            if (state == "clean")
            {
                Block? coatedBlock = GetBlockForState(world, "coated");
                if (coatedBlock == null)
                {
                    ServerDebugLog.Notify(world.Api, "plate-interact: pour-complete state=clean → declined: plate-coated block not found");
                    return false;
                }
                world.BlockAccessor.SetBlock(coatedBlock.Id, pos);
                world.BlockAccessor.MarkBlockDirty(pos);
                return true;
            }

            if (state == "coated")
            {
                return GiveSensitizedPlateAndRemoveBlock(world, player, pos);
            }

            return false;
        }

        private bool TryBuildHeldChemicalHint(IWorldAccessor world, BlockPos? pos, string state, IPlayer? player, out WorldInteraction interaction)
        {
            interaction = default!;
            if (pos == null || player == null) return false;

            if (!TryGetChemicalForState(state, out AssetLocation chemicalCode)) return false;
            if (!CanApplySensitizationChemical(player, chemicalCode)) return false;

            string actionLangCode = state == "clean"
                ? "photochemistry:heldhelp-coatglassplate"
                : "photochemistry:heldhelp-plate-sensitize-next";

            Item? required = world.GetItem(chemicalCode);
            if (required == null) return false;

            interaction = new WorldInteraction
            {
                ActionLangCode = actionLangCode,
                MouseButton = EnumMouseButton.Right,
                Itemstacks = [new ItemStack(required, SensitizationChemicalAmount)]
            };
            return true;
        }

        private static bool GiveSensitizedPlateAndRemoveBlock(IWorldAccessor world, IServerPlayer sp, BlockPos pos)
        {
            Item? sensitizedItem = world.GetItem(_sensitizedPlateItemCode);
            if (sensitizedItem == null) return false;

            ItemStack sensitizedPlate = new ItemStack(sensitizedItem, 1);
            PlateAttributes.SetStage(sensitizedPlate, PlateStage.Sensitized);
            PlateAttributes.SetChemistry(sensitizedPlate, PlateAttributes.ChemistryCollodion);
            PlateAttributes.SetNameLangCode(sensitizedPlate, "photochemistry:plate-name-sensitized");
            PlateDryingTransition.ResetTimer(world, sensitizedPlate, PlateDryingTransition.ResolveWetDurationHours(world.Api));

            if (!sp.InventoryManager.TryGiveItemstack(sensitizedPlate))
            {
                world.SpawnItemEntity(sensitizedPlate, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            return true;
        }

        private void GiveItemAndRemoveBlock(IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp && TryCreatePlateItemStack(world, pos, out ItemStack stack))
            {
                if (!sp.InventoryManager.TryGiveItemstack(stack))
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
        }

        private bool HandlePolishInteractionStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (!IsHoldingPlainCloth(byPlayer)) return false;
            if (secondsUsed < GetPolishSeconds()) return true;

            if (world.Side != EnumAppSide.Server) return false;
            Block? cleanBlock = GetBlockForState(world, "clean");
            if (cleanBlock == null) return false;

            bool isCreative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            int consumeCount = GetPlainClothConsumeCount();
            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (!isCreative && consumeCount > 0)
            {
                if (activeSlot?.Itemstack == null || activeSlot.Itemstack.StackSize < consumeCount)
                {
                    return false;
                }
            }

            world.BlockAccessor.SetBlock(cleanBlock.Id, pos);
            world.BlockAccessor.MarkBlockDirty(pos);

            if (!isCreative && consumeCount > 0)
            {
                activeSlot!.TakeOut(consumeCount);
                activeSlot.MarkDirty();
            }

            return false;
        }

        private WorldInteraction[] BuildPolishInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            Item? clothItem = world.GetItem(_plainClothCode);
            if (clothItem == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

            return
            [
                new WorldInteraction
                {
                    ActionLangCode = "photochemistry:heldhelp-cleanroughglass",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = [new ItemStack(clothItem)]
                }
            ];
        }

        private static bool IsHoldingPlainCloth(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            return held?.Collectible?.Code != null && held.Collectible.Code == _plainClothCode;
        }

        private static bool IsEmptyHand(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            return activeSlot?.Itemstack == null;
        }
    }
}