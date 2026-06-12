using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Collodion.AdminTooling;


namespace Collodion.Plates.Blocks
{
    public sealed partial class BlockGlassPlate
    {
        private const float DefaultPolishSeconds = 2.0f;
        private static readonly AssetLocation _glassPlateItemCode = new("collodion", "glassplate");
        private CollodionConfig? Cfg => CollodionConfigAccess.ResolveConfig(api);
        private PlateProcessingConfig? PlateCfg => Cfg?.PlateProcessing;

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
            return world?.GetBlock(new AssetLocation(Code?.Domain ?? "collodion", $"plate-{state}"));
        }

        private bool TryCreatePlateItemStack(IWorldAccessor world, BlockPos pos, out ItemStack stack)
        {
            stack = default!;

            string state = GetPlateState();
            Item? item = world?.GetItem(_glassPlateItemCode);
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

            if (stage == PlateStage.Rough)
            {
                PlateAttributes.SetNameLangCode(stack, "collodion:plate-name-glass");
            }
            else if (stage == PlateStage.Clean)
            {
                PlateAttributes.SetNameLangCode(stack, "collodion:plate-name-glass-clean");
            }

            return true;
        }
    }
}

