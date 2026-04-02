using System;
using System.Collections.Generic;

namespace Collodion
{
    /// <summary>
    /// Holds all registered <see cref="PhotographyProcessDefinition"/>s and resolves a process
    /// by ID, falling back to the built-in iodide default for legacy plates with no process attribute.
    /// </summary>
    public sealed class ProcessRegistry
    {
        // ---------------------------------------------------------------------------
        // Shared sub-parameters (reused across multiple built-in processes)
        // ---------------------------------------------------------------------------

        private static readonly ExposureParameters DefaultExposure = new ExposureParameters(
            minExposureSeconds: 0.5,
            maxExposureSeconds: 8.0);

        /// <summary>
        /// Standard development pipeline used by iodide and chloride processes.
        /// Ingredient codes are placeholders until per-process chemistry is finalised.
        /// </summary>
        private static readonly DevelopmentParameters StandardDevelopment = new DevelopmentParameters(
            developerPortionCode:  "collodion:developerportion",
            developerPourCount:    5,
            developerAmountPerPour: 40,
            fixerPortionCode:      "collodion:fixerportion",
            fixerAmountPerPour:    40,
            requiresWaterRinse:    true,
            wetDurationMultiplier: 1.0);

        /// <summary>
        /// Bromide development pipeline: same chemicals as standard but the plate never dries.
        /// Ingredient codes are placeholders until per-process chemistry is finalised.
        /// </summary>
        private static readonly DevelopmentParameters BromideDevelopment = new DevelopmentParameters(
            developerPortionCode:  "collodion:developerportion",
            developerPourCount:    5,
            developerAmountPerPour: 40,
            fixerPortionCode:      "collodion:fixerportion",
            fixerAmountPerPour:    40,
            requiresWaterRinse:    true,
            wetDurationMultiplier: 0.0);

        // ---------------------------------------------------------------------------
        // Built-in process definitions
        // ---------------------------------------------------------------------------

        /// <summary>
        /// The iodide wet-plate process — default for all legacy plates with no process attribute.
        /// Requires collodion coating then a silver bath before exposure.
        /// Sensitization ingredient codes are placeholders pending item definition.
        /// </summary>
        public static readonly PhotographyProcessDefinition DefaultProcess =
            new PhotographyProcessDefinition(
                id:                   PlateStateAttributes.DefaultProcessId,
                displayName:          "Iodide Wet Plate",
                defaultEffectsProfile: "iodide",
                sensitizationSteps:   new SensitizationStep[]
                {
                    new SensitizationStep("coat-collodion", "collodion:collodionportion",      40, "sensitizing"),
                    new SensitizationStep("silver-bath",    "collodion:silversolutionportion",  40, "sensitizing"),
                },
                exposure:    DefaultExposure,
                development: StandardDevelopment);

        /// <summary>
        /// Silver chloride (POP) process — direct silver sensitizing, no collodion coat required.
        /// Sensitization ingredient codes are placeholders pending item definition.
        /// </summary>
        public static readonly PhotographyProcessDefinition ChlorideProcess =
            new PhotographyProcessDefinition(
                id:                   "chloride",
                displayName:          "Silver Chloride Plate",
                defaultEffectsProfile: "chloride",
                sensitizationSteps:   new SensitizationStep[]
                {
                    new SensitizationStep("silver-chloride-bath", "collodion:silverchloridebath", 40, "sensitizing"),
                },
                exposure:    DefaultExposure,
                development: StandardDevelopment);

        /// <summary>
        /// Gelatin silver bromide process — direct sensitizing, no collodion coat, plate never dries.
        /// Sensitization ingredient codes are placeholders pending item definition.
        /// </summary>
        public static readonly PhotographyProcessDefinition BromideProcess =
            new PhotographyProcessDefinition(
                id:                   "bromide",
                displayName:          "Silver Bromide Plate",
                defaultEffectsProfile: "bromide",
                sensitizationSteps:   new SensitizationStep[]
                {
                    new SensitizationStep("bromide-emulsion", "collodion:bromideemulsion", 40, "sensitizing"),
                },
                exposure:    DefaultExposure,
                development: BromideDevelopment);

        // ---------------------------------------------------------------------------
        // Registry instance
        // ---------------------------------------------------------------------------

        private readonly Dictionary<string, PhotographyProcessDefinition> _processes =
            new Dictionary<string, PhotographyProcessDefinition>(StringComparer.OrdinalIgnoreCase);

        public ProcessRegistry()
        {
            _processes[DefaultProcess.Id]  = DefaultProcess;
            _processes[ChlorideProcess.Id] = ChlorideProcess;
            _processes[BromideProcess.Id]  = BromideProcess;
        }

        /// <summary>
        /// Registers a process definition, overwriting any existing entry with the same ID.
        /// </summary>
        public void RegisterProcess(PhotographyProcessDefinition definition)
        {
            _processes[definition.Id] = definition;
        }

        /// <summary>
        /// Returns the definition for <paramref name="processId"/>, or <see cref="DefaultProcess"/>
        /// if the ID is null, empty, or unrecognized.
        /// </summary>
        public PhotographyProcessDefinition ResolveOrDefault(string? processId)
        {
            if (!string.IsNullOrEmpty(processId) && _processes.TryGetValue(processId, out var def))
                return def;

            return DefaultProcess;
        }

        /// <summary>All currently registered process definitions, keyed by ID.</summary>
        public IReadOnlyDictionary<string, PhotographyProcessDefinition> AllProcesses => _processes;
    }
}
