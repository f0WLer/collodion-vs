using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Photocore.Configuration;


namespace Photocore.Plates
{
    public sealed partial class BlockGlassPlate
    {
        private const float DefaultPolishSeconds = 2.0f;
        private PhotocoreConfig? Cfg => PhotocoreConfigAccess.ResolveConfig(api);
        private PlateProcessingConfig? PlateCfg => Cfg?.PlateProcessing;

        // Substrate-specific knobs, read from block JSON with glass-preserving defaults so this one
        // block class serves any plate-like substrate (glass today; paper later) without a fork.
        // "plateSubstrate" scopes which sensitization recipes can start here; "sensitizedItemCode" is
        // the item minted when sensitization completes.
        private string Substrate => Attributes?["plateSubstrate"]?.AsString("glass") ?? "glass";
        private AssetLocation SensitizedItemCode => new(
            Attributes?["sensitizedItemCode"]?.AsString("photocore:sensitizedplate") ?? "photocore:sensitizedplate");
        // Block-code prefix for this substrate's state blocks ("plate" → plate-rough/clean/coated) and the
        // item minted when the placed block is picked up. Glass defaults; paper overrides via block JSON.
        private string StateBlockPrefix => Attributes?["stateBlockPrefix"]?.AsString("plate") ?? "plate";
        private AssetLocation BaseItemCode => new(
            Attributes?["baseItemCode"]?.AsString("photocore:glassplate") ?? "photocore:glassplate");

        private float GetPolishSeconds()
        {
            float seconds = PlateCfg?.PolishSeconds ?? DefaultPolishSeconds;
            if (seconds < 0f) seconds = 0f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        private float GetSensitizationPourSeconds()
        {
            float seconds = PlateCfg?.SensitizationPourSeconds ?? 1.5f;
            if (seconds < 0f) seconds = 0f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        private int GetClothConsumeCount()
        {
            int amount = PlateCfg?.ClothConsumedPerPolish ?? 0;
            if (amount < 0) amount = 0;
            return amount;
        }

        private string GetPlateState()
        {
            string? variantState = Variant?["state"];
            if (!string.IsNullOrEmpty(variantState)) return variantState;

            string path = Code?.Path ?? "";
            if (path.EndsWith("-clean")) return "clean";
            if (path.EndsWith("-coated")) return "coated";
            return "rough";
        }

        private Block? GetBlockForState(IWorldAccessor world, string state)
        {
            return world?.GetBlock(new AssetLocation(Code?.Domain ?? "photocore", $"{StateBlockPrefix}-{state}"));
        }

        // SetBlock discards the old block entity and builds a fresh one, so anything belonging to the
        // glass itself rather than to the state being left behind has to be lifted across by hand.
        // Every rough/clean/coated transition goes through here so there is one place to do that.
        private static void SwapPlateStateBlock(IWorldAccessor world, BlockPos pos, Block target)
        {
            int reclaims = (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityGlassPlate)?.ReclaimCount ?? 0;

            world.BlockAccessor.SetBlock(target.Id, pos);

            if (reclaims > 0 && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityGlassPlate be)
            {
                be.ReclaimCount = reclaims;
                be.MarkDirty(true);
            }
        }

        private bool TryCreatePlateItemStack(IWorldAccessor world, BlockPos pos, out ItemStack stack)
        {
            stack = default!;

            string state = GetPlateState();
            Item? item = world?.GetItem(BaseItemCode);
            if (item == null) return false;

            stack = new ItemStack(item);

            PlateStage stage = state switch
            {
                "clean" => PlateStage.Clean,
                "coated" => PlateStage.Sensitizing,
                _ => PlateStage.Rough
            };

            PlateAttributes.SetStage(stack, stage);
            stack.Attributes.SetString("plateBlockState", state);

            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityGlassPlate be)
            {
                // Carry the fine sensitization progress so a picked-up coated plate resumes correctly when re-placed.
                if (state == "coated" && be.ChemistryId != null)
                {
                    stack.Attributes.SetString("plateChemistry", be.ChemistryId);
                    stack.Attributes.SetInt("plateStep", be.StepIndex);
                }

                // Unlike chemistry, this belongs to the glass itself, so it rides every state out.
                if (be.ReclaimCount > 0) PlateAttributes.SetReclaimCount(stack, be.ReclaimCount);
            }

            // Glass-specific display names; other substrates fall back to their own itemtype name.
            if (Substrate == "glass")
            {
                if (stage == PlateStage.Rough)
                    PlateAttributes.SetNameLangCode(stack, "photocore:plate-name-glass");
                else if (stage == PlateStage.Clean)
                    PlateAttributes.SetNameLangCode(stack, "photocore:plate-name-glass-clean");
            }

            return true;
        }
    }
}

