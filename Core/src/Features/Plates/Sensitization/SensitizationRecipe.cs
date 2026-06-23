using Vintagestory.API.Common;

namespace Photochemistry.Plates
{
    // How a single sensitization step is applied to the plate.
    internal enum SensitizationInteractionType
    {
        PourLiquid, // hold a liquid container with >= Amount units of Ingredient (timed pour)
        ApplySolid  // hold >= Amount of the solid item Ingredient (consumed)
    }

    // One step in a chemistry's sensitization sequence.
    internal sealed class SensitizationStep
    {
        public SensitizationInteractionType Type { get; init; }
        public AssetLocation Ingredient { get; init; } = default!;
        public int Amount { get; init; }
        public AssetLocation? Sound { get; init; }
        public string ActionLangCode { get; init; } = "";
    }

    // The ordered steps that turn a clean substrate into a sensitized plate of one chemistry.
    // The rough->clean polish is shared prep and is not part of any recipe.
    internal sealed class SensitizationRecipe
    {
        public string ChemistryId { get; init; } = "";
        // Which substrate this recipe applies to (e.g. "glass", "paper"). Scopes start-step matching so
        // a glass plate never branches into a paper recipe and vice-versa. Defaults to glass.
        public string Substrate { get; init; } = "glass";
        public IReadOnlyList<SensitizationStep> Steps { get; init; } = System.Array.Empty<SensitizationStep>();
    }

    // Head-populated registry of sensitization recipes. Core registers iodide; superset heads add more.
    // A chemistry is obtainable iff a recipe is registered for it (so this also gates availability).
    internal static class SensitizationRegistry
    {
        private static readonly List<SensitizationRecipe> _recipes = new();

        internal static void Register(SensitizationRecipe recipe)
        {
            _recipes.RemoveAll(r => r.ChemistryId == recipe.ChemistryId);
            _recipes.Add(recipe);
        }

        // The chemistries the current head makes obtainable, in registration order (Core registers iodide;
        // superset heads add more). Used to populate the physics tuner's chemistry selector, so baseline
        // collodion offers only iodide while kosphotography offers iodide/chloride/bromide.
        internal static IReadOnlyList<string> RegisteredChemistries()
        {
            List<string> result = new();
            foreach (SensitizationRecipe r in _recipes)
                if (!string.IsNullOrEmpty(r.ChemistryId) && !result.Contains(r.ChemistryId)) result.Add(r.ChemistryId);
            return result;
        }

        internal static SensitizationRecipe? ByChemistry(string? chemistryId)
        {
            if (string.IsNullOrEmpty(chemistryId)) return null;
            foreach (SensitizationRecipe r in _recipes)
                if (r.ChemistryId == chemistryId) return r;
            return null;
        }

        // Which chemistry a clean substrate branches into: the recipe (for this substrate) whose first
        // step the player can apply. Substrate-scoped so glass and paper recipes never cross-match.
        internal static SensitizationRecipe? MatchStartingStep(string substrate, ItemSlot? heldSlot)
        {
            foreach (SensitizationRecipe r in _recipes)
                if (r.Substrate == substrate && r.Steps.Count > 0 && SensitizationStepIO.CanApply(heldSlot, r.Steps[0])) return r;
            return null;
        }
    }

    // Validates and consumes the held item for a sensitization step. PourLiquid reuses the liquid-portion
    // helpers; ApplySolid consumes whole items (mirrors the cloth-consume polish path).
    internal static class SensitizationStepIO
    {
        internal static bool CanApply(ItemSlot? slot, SensitizationStep step)
        {
            ItemStack? held = slot?.Itemstack;
            if (held == null) return false;
            return step.Type switch
            {
                SensitizationInteractionType.PourLiquid => PlateChemicalUtil.HasConsumableChemical(slot, step.Ingredient, step.Amount),
                SensitizationInteractionType.ApplySolid => held.Collectible?.Code == step.Ingredient && held.StackSize >= step.Amount,
                _ => false
            };
        }

        internal static bool Consume(ItemSlot? slot, SensitizationStep step, bool isCreative)
        {
            if (isCreative) return true;
            ItemStack? held = slot?.Itemstack;
            if (held == null) return false;

            switch (step.Type)
            {
                case SensitizationInteractionType.PourLiquid:
                    return PlateChemicalUtil.TryConsumeChemical(slot, step.Ingredient, step.Amount);
                case SensitizationInteractionType.ApplySolid:
                    if (held.Collectible?.Code != step.Ingredient || held.StackSize < step.Amount) return false;
                    slot!.TakeOut(step.Amount);
                    slot.MarkDirty();
                    return true;
                default:
                    return false;
            }
        }
    }
}
