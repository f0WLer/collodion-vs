using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Photochemistry.AdminTooling;
using Photochemistry.Configuration;


namespace Photochemistry.Plates
{
    public sealed partial class BlockGlassPlate
    {
        private const float DefaultPolishSeconds = 2.0f;
        private PhotochemistryConfig? Cfg => PhotochemistryConfigAccess.ResolveConfig(api);
        private PlateProcessingConfig? PlateCfg => Cfg?.PlateProcessing;

        // Substrate-specific knobs, read from block JSON with glass-preserving defaults so this one
        // block class serves any plate-like substrate (glass today; paper later) without a fork.
        // "plateSubstrate" scopes which sensitization recipes can start here; "sensitizedItemCode" is
        // the item minted when sensitization completes.
        private string Substrate => Attributes?["plateSubstrate"]?.AsString("glass") ?? "glass";
        private AssetLocation SensitizedItemCode => new(
            Attributes?["sensitizedItemCode"]?.AsString("photochemistry:sensitizedplate") ?? "photochemistry:sensitizedplate");
        // Block-code prefix for this substrate's state blocks ("plate" → plate-rough/clean/coated) and the
        // item minted when the placed block is picked up. Glass defaults; paper overrides via block JSON.
        private string StateBlockPrefix => Attributes?["stateBlockPrefix"]?.AsString("plate") ?? "plate";
        private AssetLocation BaseItemCode => new(
            Attributes?["baseItemCode"]?.AsString("photochemistry:glassplate") ?? "photochemistry:glassplate");

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

        private int GetPlainClothConsumeCount()
        {
            if (PlateCfg?.ConsumePlainClothOnPolish != true) return 0;

            int amount = PlateCfg.PlainClothConsumedPerPolish;
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
            return world?.GetBlock(new AssetLocation(Code?.Domain ?? "photochemistry", $"{StateBlockPrefix}-{state}"));
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

            // Carry the fine sensitization progress so a picked-up coated plate resumes correctly when re-placed.
            if (state == "coated" && world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityGlassPlate be && be.ChemistryId != null)
            {
                stack.Attributes.SetString("plateChemistry", be.ChemistryId);
                stack.Attributes.SetInt("plateStep", be.StepIndex);
            }

            // Glass-specific display names; other substrates fall back to their own itemtype name.
            if (Substrate == "glass")
            {
                if (stage == PlateStage.Rough)
                    PlateAttributes.SetNameLangCode(stack, "photochemistry:plate-name-glass");
                else if (stage == PlateStage.Clean)
                    PlateAttributes.SetNameLangCode(stack, "photochemistry:plate-name-glass-clean");
            }

            return true;
        }
    }
}

