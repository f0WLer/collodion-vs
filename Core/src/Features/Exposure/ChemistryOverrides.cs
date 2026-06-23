namespace Photochemistry.Exposure
{
    /// <summary>
    /// Per-chemistry exposure-parameter overrides, edited live in the physics tuner and persisted to disk.
    /// Each value is NaN when the chemistry should inherit its hardcoded <see cref="PlateProcessProfile"/>
    /// default, or a concrete value that wins over the default. One instance exists per chemistry, so tuning
    /// one chemistry never bleeds into another. Public properties so Json.NET round-trips it for persistence.
    /// </summary>
    internal sealed class ChemistryOverrides
    {
        public float DevStrength  { get; set; } = float.NaN;
        public float HDGamma      { get; set; } = float.NaN;
        public float RedSens      { get; set; } = float.NaN;
        public float GreenSens    { get; set; } = float.NaN;
        public float BlueSens     { get; set; } = float.NaN;
        public float Inertia      { get; set; } = float.NaN;
        public float Reciprocity  { get; set; } = float.NaN;
        public float ExposureGain { get; set; } = float.NaN;

        // Shutter timing: how many virtual frames are drawn over how many seconds. Not driven by the
        // accumulation sliders — edited via the two timing boxes — but persisted in the same profile.
        // SampleCount is stored as a float for a uniform NaN-means-inherit sentinel; rounded on use.
        public float DurationSeconds { get; set; } = float.NaN;
        public float SampleCount     { get; set; } = float.NaN;

        /// <summary>Restores every parameter to "inherit the profile default".</summary>
        public void Reset() =>
            DevStrength = HDGamma = RedSens = GreenSens = BlueSens = Inertia = Reciprocity = ExposureGain
                = DurationSeconds = SampleCount = float.NaN;

        /// <summary>Copies another set's values onto this one in place (used when applying persisted tuning).</summary>
        public void CopyFrom(ChemistryOverrides other)
        {
            DevStrength = other.DevStrength; HDGamma = other.HDGamma;
            RedSens = other.RedSens; GreenSens = other.GreenSens; BlueSens = other.BlueSens;
            Inertia = other.Inertia; Reciprocity = other.Reciprocity; ExposureGain = other.ExposureGain;
            DurationSeconds = other.DurationSeconds; SampleCount = other.SampleCount;
        }

        /// <summary>A concrete copy with every NaN replaced by the profile default — what gets persisted, so a
        /// saved chemistry fully defines its parameters (and a fresh install with no file uses the hardcoded defaults).</summary>
        public ChemistryOverrides Materialize(in PlateProcessProfile p) => new()
        {
            DevStrength     = float.IsNaN(DevStrength)     ? p.DevelopmentStrength  : DevStrength,
            HDGamma         = float.IsNaN(HDGamma)         ? p.HDGamma             : HDGamma,
            RedSens         = float.IsNaN(RedSens)         ? p.RedSensitivity      : RedSens,
            GreenSens       = float.IsNaN(GreenSens)       ? p.GreenSensitivity    : GreenSens,
            BlueSens        = float.IsNaN(BlueSens)        ? p.BlueSensitivity     : BlueSens,
            Inertia         = float.IsNaN(Inertia)         ? p.InertiaPoint        : Inertia,
            Reciprocity     = float.IsNaN(Reciprocity)     ? p.ReciprocityExponent : Reciprocity,
            ExposureGain    = float.IsNaN(ExposureGain)    ? p.ExposureGain        : ExposureGain,
            DurationSeconds = float.IsNaN(DurationSeconds) ? p.DurationSeconds     : DurationSeconds,
            SampleCount     = float.IsNaN(SampleCount)     ? p.SampleCount         : SampleCount,
        };
    }
}
