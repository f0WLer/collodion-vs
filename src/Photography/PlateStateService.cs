using Vintagestory.API.Common;

namespace Collodion
{
    /// <summary>
    /// Reads and writes per-plate state attributes (process ID and lifecycle stage) on an
    /// <see cref="ItemStack"/>, including legacy-fallback behavior for pre-multi-process plates.
    /// </summary>
    public static class PlateStateService
    {
        /// <summary>
        /// Returns the process ID stored on <paramref name="stack"/>, or
        /// <see cref="PlateStateAttributes.DefaultProcessId"/> when the attribute is absent —
        /// preserving behavior for legacy plates.
        /// </summary>
        public static string GetProcessId(ItemStack? stack)
        {
            string? stored = stack?.Attributes?.GetString(PlateStateAttributes.ProcessId);
            return string.IsNullOrEmpty(stored) ? PlateStateAttributes.DefaultProcessId : stored!;
        }

        /// <summary>
        /// Ensures a concrete process ID exists on <paramref name="stack"/>.
        /// Legacy stacks with no process attribute are upgraded to the default process ID.
        /// Returns the process ID that is (or was already) on the stack.
        /// </summary>
        public static string EnsureProcessId(ItemStack stack)
        {
            string? stored = stack.Attributes.GetString(PlateStateAttributes.ProcessId);
            if (string.IsNullOrEmpty(stored))
            {
                stack.Attributes.SetString(PlateStateAttributes.ProcessId, PlateStateAttributes.DefaultProcessId);
                return PlateStateAttributes.DefaultProcessId;
            }

            return stored!;
        }

        /// <summary>Writes the process ID attribute on <paramref name="stack"/>.</summary>
        public static void SetProcessId(ItemStack stack, string processId)
        {
            stack.Attributes.SetString(PlateStateAttributes.ProcessId, processId);
        }

        /// <summary>
        /// Returns the current <see cref="PlateStage"/> for <paramref name="stack"/>.
        /// Returns <see cref="PlateStage.Unknown"/> when the stage attribute is absent or unrecognized.
        /// </summary>
        public static PlateStage GetStage(ItemStack? stack)
        {
            string? raw = stack?.Attributes?.GetString(PlateStateAttributes.Stage);
            return PlateStageUtil.FromAttributeString(raw);
        }

        /// <summary>
        /// Ensures a valid stage exists on <paramref name="stack"/>; if missing/unknown,
        /// writes <paramref name="fallbackStage"/>.
        /// </summary>
        public static PlateStage EnsureStage(ItemStack stack, PlateStage fallbackStage)
        {
            PlateStage stage = GetStage(stack);
            if (stage == PlateStage.Unknown)
            {
                SetStage(stack, fallbackStage);
                return fallbackStage;
            }

            return stage;
        }

        /// <summary>Writes the lifecycle stage attribute on <paramref name="stack"/>.</summary>
        public static void SetStage(ItemStack stack, PlateStage stage)
        {
            stack.Attributes.SetString(PlateStateAttributes.Stage, PlateStageUtil.ToAttributeString(stage));
        }
    }
}
