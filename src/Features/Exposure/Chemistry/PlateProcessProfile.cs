namespace Collodion.Exposure
{
    /// <summary>
    /// Immutable emulsion parameters for a single wet-plate chemistry variant.
    /// Defines shutter timing (<see cref="DurationSeconds"/>, <see cref="SampleCount"/>) and the
    /// spectral sensitivity and H&amp;D curve values that shape how the accumulation buffer develops.
    /// </summary>
    internal readonly struct PlateProcessProfile
    {
        internal readonly string Name;
        internal readonly float DurationSeconds;
        internal readonly int   SampleCount;
        internal readonly float RedSensitivity;
        internal readonly float GreenSensitivity;
        internal readonly float BlueSensitivity;
        internal readonly float DevelopmentStrength;
        internal readonly float HDGamma;
        internal readonly float InertiaPoint; // normalised E below which density is zero
        internal readonly float ReciprocityExponent; // Schwarzschild p; <1.0 → long-exposure underperformance
        // Multiplied onto invRef during Resolve(). Calibrates mid-tone brightness independently of
        // the full-white white-point — game scenes rarely hit s=1.0, so 1.0 would leave them dark.
        internal readonly float ExposureGain;

        /// <summary>Wall-clock seconds between consecutive virtual renders at normal cadence.</summary>
        internal float SampleInterval => DurationSeconds / SampleCount;

        /// <summary>ISO-equivalent film speed: reciprocal of <see cref="DurationSeconds"/>.</summary>
        internal float IsoEquivalent => 1f / DurationSeconds;

        internal PlateProcessProfile(
            string name, float durationSeconds, int sampleCount,
            float redSensitivity, float greenSensitivity, float blueSensitivity,
            float developmentStrength, float hdGamma, float inertiaPoint = 0f,
            float reciprocityExponent = 1f, float exposureGain = 1.75f)
        {
            Name = name;
            DurationSeconds = durationSeconds;
            SampleCount = sampleCount;
            RedSensitivity = redSensitivity;
            GreenSensitivity = greenSensitivity;
            BlueSensitivity = blueSensitivity;
            DevelopmentStrength = developmentStrength;
            HDGamma = hdGamma;
            InertiaPoint = inertiaPoint;
            ReciprocityExponent = reciprocityExponent;
            ExposureGain = exposureGain;
        }

        /// <summary>
        /// Primitive silver chloride: blue-only sensitivity, very slow (~25 s), steep H&amp;D curve.
        /// Produces high-contrast images with compressed shadow range — demanding to expose correctly.
        /// </summary>
        internal static readonly PlateProcessProfile Chloride = new PlateProcessProfile(
            "Chloride", durationSeconds: 25f, sampleCount: 128,
            redSensitivity: 0.04f, greenSensitivity: 0.35f, blueSensitivity: 1.00f,
            developmentStrength: 5.0f, hdGamma: 1.50f, inertiaPoint: 0.20f,
            reciprocityExponent: 0.70f);

        /// <summary>
        /// Wet-plate collodion iodide: expanded spectral response, moderate speed (~8 s).
        /// Balanced H&amp;D curve — forgiving enough for skilled use, demanding enough to matter.
        /// </summary>
        internal static readonly PlateProcessProfile Iodide = new PlateProcessProfile(
            "Iodide", durationSeconds: 8f, sampleCount: 40,
            redSensitivity: 0.12f, greenSensitivity: 0.45f, blueSensitivity: 1.00f,
            developmentStrength: 8.0f, hdGamma: 1.10f, inertiaPoint: 0.10f,
            reciprocityExponent: 0.90f);

        /// <summary>
        /// Silver bromide dry plate: near-panchromatic, fast (~3 s), gradual H&amp;D shoulder.
        /// Wide exposure latitude and rich maximum density — the most capable tier.
        /// </summary>
        internal static readonly PlateProcessProfile Bromide = new PlateProcessProfile(
            "Bromide", durationSeconds: 3f, sampleCount: 32,
            redSensitivity: 0.30f, greenSensitivity: 0.59f, blueSensitivity: 1.00f,
            developmentStrength: 12.0f, hdGamma: 0.85f, inertiaPoint: 0.04f,
            reciprocityExponent: 1.00f);

        /// <summary>Parses a chemistry name (case-insensitive) into a <see cref="PlateProcessProfile"/>. Returns <see langword="false"/> when the name is unrecognised.</summary>
        internal static bool TryParse(string name, out PlateProcessProfile profile)
        {
            switch (name.ToLowerInvariant())
            {
                case "chloride": profile = Chloride; return true;
                case "iodide":   profile = Iodide;   return true;
                case "bromide":  profile = Bromide;  return true;
                default:         profile = default;  return false;
            }
        }
    }
}
