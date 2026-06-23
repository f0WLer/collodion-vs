using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Photochemistry.Plates.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        private const int MaxDeveloperPours = 5;

        // True when the plate's itemtype declares the paper-print medium (an opaque reflective positive)
        // rather than the default silver-on-glass density map. Drives opaque-vs-transparent render choices
        // across the held item, the development tray, frames, and placed plate blocks.
        public static bool IsPaperMedium(ItemStack? stack)
            => string.Equals(
                stack?.Collectible?.Attributes?["plateMedium"]?.AsString(null),
                "paperprint", System.StringComparison.OrdinalIgnoreCase);

        // Resolves effective developer progress for render-stage visuals, clamped to process limits.
        private static void ResolveDevelopedRenderProgress(ICoreClientAPI capi, ItemStack itemstack, out int developPours, out int maxDeveloperPours)
        {
            maxDeveloperPours = MaxDeveloperPours;

            if (PlateAttributes.GetStage(itemstack) == PlateStage.Developed || PlateAttributes.GetStage(itemstack) == PlateStage.Finished)
            {
                developPours = maxDeveloperPours;
                return;
            }

            if (PlateAttributes.GetStage(itemstack) == PlateStage.Developing)
            {
                developPours = PlateAttributes.GetDevelopmentApplications(itemstack);
            }
            else
            {
                developPours = 0;
            }

            // Keep stage-based progress stable for cache keys and derived render variants.
            if (developPours < 0) developPours = 0;
            if (developPours > maxDeveloperPours) developPours = maxDeveloperPours;
        }

    }
}

