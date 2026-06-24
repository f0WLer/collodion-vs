using Vintagestory.API.Common;

namespace Photochemistry.Plates
{
    /// <summary>
    /// Shared camera load/exposure plate eligibility rules used by both client and server paths.
    /// Keeps stage-first logic in one place for consolidated plate item families.
    /// </summary>
    public static class CameraEligibility
    {
        // A camera-loadable item is any plate itemtype declaring the "sensitized" role (glass or paper),
        // rather than a hardcoded item code — so new substrates work without touching Core.
        private const string SensitizedRole = "sensitized";

        private static bool IsSensitizedItem(ItemStack? stack)
            => string.Equals(PlateAttributes.GetItemRole(stack), SensitizedRole, StringComparison.OrdinalIgnoreCase);

        // Lightweight pre-check on the compact loaded-plate code string stored on the camera. Any
        // "sensitized*" substrate code passes; the authoritative gate is IsPlateExposable on the stack.
        public static bool IsLoadedCodeSensitized(string? loadedCode)
        {
            if (string.IsNullOrWhiteSpace(loadedCode)) return false;
            int slash = loadedCode.IndexOf(':');
            string path = slash >= 0 ? loadedCode[(slash + 1)..] : loadedCode;
            return path.StartsWith("sensitized", StringComparison.OrdinalIgnoreCase);
        }

        // Checks whether an item stack is a loadable sensitized plate for camera insertion.
        public static bool CanLoadIntoCamera(ItemStack? stack)
        {
            if (!IsSensitizedItem(stack)) return false;

            PlateStage stage = PlateAttributes.GetStage(stack);
            return stage == PlateStage.Sensitized || stage == PlateStage.Exposed
                || stage == PlateStage.Exposing  || stage == PlateStage.ExposurePaused;
        }

        // Checks whether a plate can start or resume accumulation (Sensitized or ExposurePaused).
        public static bool IsPlateExposable(ItemStack? stack)
        {
            if (!IsSensitizedItem(stack)) return false;

            PlateStage stage = PlateAttributes.GetStage(stack);
            return stage == PlateStage.Sensitized || stage == PlateStage.ExposurePaused;
        }
    }
}
