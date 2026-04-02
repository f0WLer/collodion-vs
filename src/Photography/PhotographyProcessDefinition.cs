using System.Collections.Generic;

namespace Collodion
{
    /// <summary>
    /// Describes the full characteristics and pipeline of a photographic plate chemistry process,
    /// covering sensitization steps, exposure constraints, and development parameters.
    /// </summary>
    public sealed class PhotographyProcessDefinition
    {
        /// <summary>Unique machine identifier; matches the <see cref="PlateStateAttributes.ProcessId"/> attribute value.</summary>
        public string Id { get; }

        /// <summary>Human-readable name shown in tooltips and commands.</summary>
        public string DisplayName { get; }

        /// <summary>
        /// Effects profile name used when rendering a finished plate produced by this process.
        /// Corresponds to a profile file at ModData/collodion/{name}.json.
        /// </summary>
        public string DefaultEffectsProfile { get; }

        /// <summary>
        /// Ordered steps the player must perform to take a clean plate to sensitized.
        /// All steps complete in sequence before the plate becomes <see cref="PlateStage.Sensitized"/>
        /// and the process ID is stamped.
        /// </summary>
        public IReadOnlyList<SensitizationStep> SensitizationSteps { get; }

        /// <summary>Exposure constraints and parameters for this process.</summary>
        public ExposureParameters Exposure { get; }

        /// <summary>Development tray pipeline parameters (chemicals, pour counts, wet duration) for this process.</summary>
        public DevelopmentParameters Development { get; }

        public PhotographyProcessDefinition(
            string id,
            string displayName,
            string defaultEffectsProfile,
            IReadOnlyList<SensitizationStep> sensitizationSteps,
            ExposureParameters exposure,
            DevelopmentParameters development)
        {
            Id = id;
            DisplayName = displayName;
            DefaultEffectsProfile = defaultEffectsProfile;
            SensitizationSteps = sensitizationSteps;
            Exposure = exposure;
            Development = development;
        }
    }
}
