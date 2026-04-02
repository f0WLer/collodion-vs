namespace Collodion
{
    /// <summary>
    /// One discrete step in the clean-to-sensitized pipeline for a photographic process.
    /// Steps are applied in order; all must complete before the plate becomes
    /// <see cref="PlateStage.Sensitized"/> and receives its process ID.
    /// </summary>
    public sealed class SensitizationStep
    {
        /// <summary>
        /// Unique identifier for this step within its process (e.g. "coat-collodion", "silver-bath").
        /// Stored on the plate as the current step index, not by string.
        /// </summary>
        public string StepId { get; }

        /// <summary>
        /// Item or liquid portion code required for this step, as a "domain:path" string
        /// (e.g. "collodion:collodionportion"). Null when no held portion is consumed.
        /// </summary>
        public string? RequiredPortionCode { get; }

        /// <summary>
        /// Units consumed per application, using the same unit system as WetPlateChemicalUtil.
        /// Ignored when <see cref="RequiredPortionCode"/> is null.
        /// </summary>
        public int RequiredAmount { get; }

        /// <summary>
        /// Block-variant stage label active while this step is in progress.
        /// Passed to SwapTrayBlockForPlateStage for intermediate visual state.
        /// </summary>
        public string IntermediateStageLabel { get; }

        public SensitizationStep(
            string stepId,
            string? requiredPortionCode,
            int requiredAmount,
            string intermediateStageLabel)
        {
            StepId = stepId;
            RequiredPortionCode = requiredPortionCode;
            RequiredAmount = requiredAmount;
            IntermediateStageLabel = intermediateStageLabel;
        }
    }
}
