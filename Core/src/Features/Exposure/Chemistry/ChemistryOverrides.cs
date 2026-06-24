namespace Photochemistry.Exposure
{
    // NaN = inherit the process-profile default. One instance per chemistry. Public so Json.NET can persist it.
    internal sealed class ChemistryOverrides
    {
        public bool Linearize       { get; set; } = true;
        public bool SpectralWeights { get; set; } = true;
        public bool HDCurve         { get; set; } = true;
        public bool Normalize       { get; set; } = false;
        public bool LogAccumulation { get; set; } = true;

        public float DevStrength  { get; set; } = float.NaN;
        public float HDGamma      { get; set; } = float.NaN;
        public float RedSens      { get; set; } = float.NaN;
        public float GreenSens    { get; set; } = float.NaN;
        public float BlueSens     { get; set; } = float.NaN;
        public float Inertia      { get; set; } = float.NaN;
        public float Reciprocity  { get; set; } = float.NaN;
        public float ExposureGain { get; set; } = float.NaN;

        // SampleCount stored as float so NaN-means-inherit applies uniformly; rounded on use.
        public float DurationSeconds { get; set; } = float.NaN;
        public float SampleCount     { get; set; } = float.NaN;

        public void Reset()
        {
            DevStrength = HDGamma = RedSens = GreenSens = BlueSens = Inertia = Reciprocity = ExposureGain
                = DurationSeconds = SampleCount = float.NaN;
            Linearize = SpectralWeights = HDCurve = LogAccumulation = true;
            Normalize = false;
        }

        // Detached copy — the dialog edits this; edits don't reach the saved profile until Save.
        public ChemistryOverrides Clone()
        {
            ChemistryOverrides copy = new();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(ChemistryOverrides other)
        {
            Linearize = other.Linearize; SpectralWeights = other.SpectralWeights; HDCurve = other.HDCurve;
            Normalize = other.Normalize; LogAccumulation = other.LogAccumulation;
            DevStrength = other.DevStrength; HDGamma = other.HDGamma;
            RedSens = other.RedSens; GreenSens = other.GreenSens; BlueSens = other.BlueSens;
            Inertia = other.Inertia; Reciprocity = other.Reciprocity; ExposureGain = other.ExposureGain;
            DurationSeconds = other.DurationSeconds; SampleCount = other.SampleCount;
        }

        // NaN→profile-default so a persisted chemistry fully defines all params. Flags have no profile-level default.
        public ChemistryOverrides Materialize(in EmulsionProfile p) => new()
        {
            Linearize = Linearize, SpectralWeights = SpectralWeights, HDCurve = HDCurve,
            Normalize = Normalize, LogAccumulation = LogAccumulation,
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

        public EmulsionProfile ApplyTimingTo(in EmulsionProfile p)
        {
            float duration = float.IsNaN(DurationSeconds) ? p.DurationSeconds : Math.Max(0.05f, DurationSeconds);
            int samples = float.IsNaN(SampleCount) ? p.SampleCount : Math.Max(1, (int)MathF.Round(SampleCount));
            return p.WithTiming(duration, samples);
        }
    }
}
