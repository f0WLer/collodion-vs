namespace Collodion
{
    /// <summary>
    /// Canonical attribute-key and default-value constants for plate state stored on
    /// <see cref="Vintagestory.API.Common.ItemStack"/>. All plate attribute reads and writes
    /// should go through <see cref="PlateStateService"/>; refer to this class only for the
    /// raw key strings.
    /// </summary>
    public static class PlateStateAttributes
    {
        /// <summary>
        /// Identifies the photography process used for this plate (e.g. "iodide", "chloride", "bromide").
        /// Absent on plates created before multi-process support; legacy fallback is <see cref="DefaultProcessId"/>.
        /// </summary>
        public const string ProcessId = "collodionProcessId";

        /// <summary>
        /// Fallback process ID written to legacy plates that carry no process attribute.
        /// Must match <see cref="ProcessRegistry.DefaultProcess"/>.Id.
        /// </summary>
        public const string DefaultProcessId = "iodide";

        /// <summary>
        /// Current lifecycle stage string. Canonical definition — use this over WetPlateAttrs.PlateStage.
        /// Values: "rough", "clean", "sensitized", "exposed", "developing", "developed", "finished".
        /// </summary>
        public const string Stage = "collodionPlateStage";
    }
}
