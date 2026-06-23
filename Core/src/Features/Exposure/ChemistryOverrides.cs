using System;

namespace Photochemistry.Exposure
{
    /// <summary>
    /// One chemistry's exposure-accumulation settings: the physics emulation flags, the per-parameter
    /// overrides (NaN = inherit the hardcoded <see cref="PlateProcessProfile"/> default), and the shutter
    /// timing. Edited live in the physics tuner and persisted to disk. One instance per chemistry, so tuning
    /// one never bleeds into another. Public properties so Json.NET round-trips it for persistence.
    /// </summary>
    internal sealed class ChemistryOverrides
    {
        // Physics emulation flags — per-chemistry. Defaults are the canonical "correct" model.
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

        // Shutter timing: how many virtual frames are drawn over how many seconds. Not driven by the
        // accumulation sliders — edited via the two timing boxes — but persisted in the same profile.
        // SampleCount is stored as a float for a uniform NaN-means-inherit sentinel; rounded on use.
        public float DurationSeconds { get; set; } = float.NaN;
        public float SampleCount     { get; set; } = float.NaN;

        /// <summary>Restores the parameters to "inherit the profile default" and the flags to the canonical model.</summary>
        public void Reset()
        {
            DevStrength = HDGamma = RedSens = GreenSens = BlueSens = Inertia = Reciprocity = ExposureGain
                = DurationSeconds = SampleCount = float.NaN;
            Linearize = SpectralWeights = HDCurve = LogAccumulation = true;
            Normalize = false;
        }

        /// <summary>A detached copy — used for the dialog's live (preview-only) working set so edits don't
        /// touch the saved profile until Save.</summary>
        public ChemistryOverrides Clone()
        {
            ChemistryOverrides copy = new();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>Copies another set's values onto this one in place (used when applying persisted tuning).</summary>
        public void CopyFrom(ChemistryOverrides other)
        {
            Linearize = other.Linearize; SpectralWeights = other.SpectralWeights; HDCurve = other.HDCurve;
            Normalize = other.Normalize; LogAccumulation = other.LogAccumulation;
            DevStrength = other.DevStrength; HDGamma = other.HDGamma;
            RedSens = other.RedSens; GreenSens = other.GreenSens; BlueSens = other.BlueSens;
            Inertia = other.Inertia; Reciprocity = other.Reciprocity; ExposureGain = other.ExposureGain;
            DurationSeconds = other.DurationSeconds; SampleCount = other.SampleCount;
        }

        /// <summary>A concrete copy with every NaN parameter replaced by the profile default — what gets persisted, so a
        /// saved chemistry fully defines its parameters (and a fresh install with no file uses the hardcoded defaults).
        /// Flags carry over as-is (they have no profile-level default).</summary>
        public ChemistryOverrides Materialize(in PlateProcessProfile p) => new()
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

        /// <summary>The given profile with this override's shutter timing applied (samples / duration), clamped.
        /// Shared by the mounted and handheld capture paths so both honour the saved per-chemistry timing.</summary>
        public PlateProcessProfile ApplyTimingTo(in PlateProcessProfile p)
        {
            float duration = float.IsNaN(DurationSeconds) ? p.DurationSeconds : Math.Max(0.05f, DurationSeconds);
            int samples = float.IsNaN(SampleCount) ? p.SampleCount : Math.Max(1, (int)MathF.Round(SampleCount));
            return p.WithTiming(duration, samples);
        }
    }
}
