using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Photocore.Plates
{
    public sealed partial class BlockGlassPlate
    {
        private static readonly AssetLocation _plainClothCode = new("game", "cloth-plain");
        private static readonly AssetLocation _polishSound = new("game:sounds/player/chalkdraw");

        // Resolves the recipe + the step the plate is waiting for. On a clean plate the chemistry is
        // chosen by the held item (which recipe's first step it matches); on a coated plate it's fixed by
        // the block entity's stored chemistry, and the next step is read from its progress.
        private bool TryResolveCurrentStep(IWorldAccessor world, BlockPos pos, ItemSlot? heldSlot,
            out SensitizationRecipe? recipe, out int completed, out SensitizationStep? step)
        {
            recipe = null; step = null; completed = 0;
            string state = GetPlateState();

            if (state == "coated")
            {
                var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityGlassPlate;
                recipe = SensitizationRegistry.ByChemistry(be?.ChemistryId);
                completed = be?.StepIndex ?? 0;
            }
            else if (state == "clean")
            {
                recipe = SensitizationRegistry.MatchStartingStep(Substrate, heldSlot);
            }
            else return false;

            if (recipe == null || completed >= recipe.Steps.Count) { recipe = null; return false; }
            step = recipe.Steps[completed];
            return true;
        }

        private bool HandleInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            string state = GetPlateState();
            ItemSlot? heldSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;

            bool isPolish = state == "rough" && IsHoldingPlainCloth(byPlayer);
            if (isPolish && !HasEnoughPolishCloth(byPlayer))
            {
                // Reject up front rather than partway through. Nothing else can claim this interaction: a
                // rough plate never resolves a sensitization step, and a hand holding cloth isn't empty.
                if (world.Side == EnumAppSide.Client)
                {
                    (world.Api as ICoreClientAPI)?.ShowChatMessage(
                        Lang.Get("photocore:msg-plate-not-enough-cloth", GetClothConsumeCount()));
                }
                return false;
            }

            SensitizationStep? sensitizeStep = null;
            if (state != "rough"
                && TryResolveCurrentStep(world, blockSel.Position, heldSlot, out _, out _, out SensitizationStep? step)
                && SensitizationStepIO.CanApply(heldSlot, step!))
            {
                sensitizeStep = step;
            }

            bool isPickup = IsEmptyHand(byPlayer);

            if (world.Side == EnumAppSide.Client)
                return isPolish || sensitizeStep != null || isPickup;

            // Empty-hand pickup wins so in-progress plates can always be recovered.
            if (isPickup)
            {
                GiveItemAndRemoveBlock(world, byPlayer, blockSel.Position);
                return true;
            }

            if (isPolish || sensitizeStep != null)
            {
                AssetLocation? sound = isPolish ? _polishSound : sensitizeStep?.Sound;
                if (sound != null)
                    world.PlaySoundAt(sound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                return true;
            }

            return false;
        }

        private bool HandleSensitizeStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            ItemSlot? heldSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (!TryResolveCurrentStep(world, pos, heldSlot, out SensitizationRecipe? recipe, out int completed, out SensitizationStep? step))
                return false;
            if (!SensitizationStepIO.CanApply(heldSlot, step!)) return false;
            if (secondsUsed < GetSensitizationPourSeconds()) return true;

            if (world.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
                AdvanceSensitization(world, sp, pos, recipe!, completed, step!, heldSlot!);

            return false;
        }

        private void AdvanceSensitization(IWorldAccessor world, IServerPlayer player, BlockPos pos,
            SensitizationRecipe recipe, int completed, SensitizationStep step, ItemSlot heldSlot)
        {
            bool isCreative = player.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (!SensitizationStepIO.Consume(heldSlot, step, isCreative)) return;

            int newCompleted = completed + 1;
            if (newCompleted >= recipe.Steps.Count)
            {
                GiveSensitizedPlateAndRemoveBlock(world, player, pos, recipe.ChemistryId);
                return;
            }

            // More steps remain: ensure the coated visual, then record progress on the block entity.
            if (GetPlateState() != "coated")
            {
                Block? coated = GetBlockForState(world, "coated");
                if (coated == null) return;
                world.BlockAccessor.SetBlock(coated.Id, pos);
            }
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityGlassPlate be)
            {
                be.ChemistryId = recipe.ChemistryId;
                be.StepIndex = newCompleted;
                be.MarkDirty(true);
            }
            world.BlockAccessor.MarkBlockDirty(pos);
        }

        private bool TryBuildSensitizationHint(IWorldAccessor world, BlockPos? pos, IPlayer? player, out WorldInteraction interaction)
        {
            interaction = default!;
            if (pos == null || player == null) return false;

            ItemSlot? heldSlot = player.InventoryManager?.ActiveHotbarSlot;
            if (!TryResolveCurrentStep(world, pos, heldSlot, out _, out _, out SensitizationStep? step)) return false;

            Item? required = world.GetItem(step!.Ingredient);
            if (required == null) return false;

            interaction = new WorldInteraction
            {
                ActionLangCode = step.ActionLangCode,
                MouseButton = EnumMouseButton.Right,
                Itemstacks = [new ItemStack(required, step.Amount)]
            };
            return true;
        }

        private bool GiveSensitizedPlateAndRemoveBlock(IWorldAccessor world, IServerPlayer sp, BlockPos pos, string chemistryId)
        {
            Item? sensitizedItem = world.GetItem(SensitizedItemCode);
            if (sensitizedItem == null) return false;

            ItemStack sensitizedPlate = new ItemStack(sensitizedItem, 1);
            PlateAttributes.SetStage(sensitizedPlate, PlateStage.Sensitized);
            PlateAttributes.SetChemistry(sensitizedPlate, chemistryId);
            // Pin the glass-plate name; other substrates fall back to their own itemtype name.
            if (Substrate == "glass")
                PlateAttributes.SetNameLangCode(sensitizedPlate, "photocore:plate-name-sensitized");
            PlateDryingTransition.ResetTimer(world, sensitizedPlate, PlateDryingTransition.ResolveWetDurationHours(world.Api, sensitizedPlate));

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
            // Re-checked every step, not just at the start: the stack can shrink mid-polish (dropped,
            // moved, used elsewhere), and the interaction should stop there rather than run to completion
            // and fall through the affordability guard below without a word.
            if (!HasEnoughPolishCloth(byPlayer)) return false;
            if (secondsUsed < GetPolishSeconds()) return true;

            if (world.Side != EnumAppSide.Server) return false;
            Block? cleanBlock = GetBlockForState(world, "clean");
            if (cleanBlock == null) return false;

            bool isCreative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            int consumeCount = GetClothConsumeCount();
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
                    ActionLangCode = "photocore:heldhelp-cleanroughglass",
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

        // Cloth is only spent when a polish completes, but whether the player can afford it has to be
        // settled before the interaction begins. Deciding it at the end means the polish plays out in full
        // and then fails silently, which ends the interaction and lets a held right-click restart it — an
        // endless polish that never cleans the plate and never explains why.
        private bool HasEnoughPolishCloth(IPlayer player)
        {
            if (player.WorldData?.CurrentGameMode == EnumGameMode.Creative) return true;

            int consumeCount = GetClothConsumeCount();
            if (consumeCount <= 0) return true;

            return (player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.StackSize ?? 0) >= consumeCount;
        }

        private static bool IsEmptyHand(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            return activeSlot?.Itemstack == null;
        }
    }
}
