namespace Photochemistry.Exposure
{
    /// <summary>
    /// Tunable physics + chemistry-override layer applied to a <see cref="GpuExposureAccumulator"/>.
    /// Physics flags pick which emulation passes run; chemistry overrides (NaN = use the active
    /// process-profile default) let the admin physics dialog probe individual parameters live.
    /// <see cref="Apply"/> copies the resolved settings onto a buffer before it accumulates.
    /// </summary>
    internal sealed class ExposurePhysicsConfig
    {
        public bool Linearize       = true;
        public bool SpectralWeights = true;
        public bool HDCurve         = true;
        public bool Normalize       = false;
        public bool LogAccumulation = true;

        // The active chemistry's overrides (NaN per param means "use the process-profile default"). The
        // physics flags above are model-wide and stay on this config; only these vary per chemistry, so the
        // owner swaps this reference when the active chemistry changes. Never null.
        public ChemistryOverrides Chem { get; set; } = new();

        public float EffectiveDevStrength(in PlateProcessProfile p)  => float.IsNaN(Chem.DevStrength)  ? p.DevelopmentStrength  : Chem.DevStrength;
        public float EffectiveHDGamma(in PlateProcessProfile p)      => float.IsNaN(Chem.HDGamma)      ? p.HDGamma             : Chem.HDGamma;
        public float EffectiveRedSens(in PlateProcessProfile p)      => float.IsNaN(Chem.RedSens)      ? p.RedSensitivity      : Chem.RedSens;
        public float EffectiveGreenSens(in PlateProcessProfile p)    => float.IsNaN(Chem.GreenSens)    ? p.GreenSensitivity    : Chem.GreenSens;
        public float EffectiveBlueSens(in PlateProcessProfile p)     => float.IsNaN(Chem.BlueSens)     ? p.BlueSensitivity     : Chem.BlueSens;
        public float EffectiveInertia(in PlateProcessProfile p)      => float.IsNaN(Chem.Inertia)      ? p.InertiaPoint        : Chem.Inertia;
        public float EffectiveReciprocity(in PlateProcessProfile p)  => float.IsNaN(Chem.Reciprocity)  ? p.ReciprocityExponent : Chem.Reciprocity;
        public float EffectiveExposureGain(in PlateProcessProfile p) => float.IsNaN(Chem.ExposureGain) ? p.ExposureGain        : Chem.ExposureGain;

        // Copies the physics flags onto a buffer.
        public void ApplyPhysics(GpuExposureAccumulator buf)
        {
            buf.LinearizeInput              = Linearize;
            buf.ApplySpectralWeights        = SpectralWeights;
            buf.ApplyHDCurve                = HDCurve;
            buf.NormalizeByActualFrameCount = Normalize;
            buf.UseLogAccumulation          = LogAccumulation;
        }

        // Copies physics flags plus chemistry (overrides resolved against the process) onto a buffer.
        public void Apply(GpuExposureAccumulator buf, in PlateProcessProfile process)
        {
            ApplyPhysics(buf);
            buf.RedSensitivity      = EffectiveRedSens(process);
            buf.GreenSensitivity    = EffectiveGreenSens(process);
            buf.BlueSensitivity     = EffectiveBlueSens(process);
            buf.DevelopmentStrength = EffectiveDevStrength(process);
            buf.HDGamma             = EffectiveHDGamma(process);
            buf.InertiaPoint        = EffectiveInertia(process);
            buf.ReciprocityExponent = EffectiveReciprocity(process);
            buf.ExposureGain        = EffectiveExposureGain(process);
        }

        // Sets a named physics flag. Returns false when the name is unrecognised.
        public bool SetPhysics(string flag, bool value)
        {
            switch (flag)
            {
                case "linearize": Linearize       = value; break;
                case "spectral":  SpectralWeights = value; break;
                case "hdcurve":   HDCurve         = value; break;
                case "normalize": Normalize       = value; break;
                case "logaccum":  LogAccumulation = value; break;
                default: return false;
            }
            return true;
        }

        // Sets a named override on the active chemistry. Returns false when the name is unrecognised.
        public bool SetChemistry(string param, float value)
        {
            switch (param)
            {
                case "devstrength": Chem.DevStrength = value; break;
                case "hdgamma":     Chem.HDGamma     = value; break;
                case "redsens":     Chem.RedSens     = value; break;
                case "greensens":   Chem.GreenSens   = value; break;
                case "bluesens":    Chem.BlueSens    = value; break;
                case "inertia":     Chem.Inertia     = value; break;
                case "reciprocity":  Chem.Reciprocity  = value; break;
                case "exposuregain": Chem.ExposureGain = value; break;
                default: return false;
            }
            return true;
        }

        // Clears the active chemistry's overrides in place, restoring its process-profile defaults.
        // Resets in place (not a new instance) so the owner's per-chemistry store keeps the same reference.
        public void ResetChemistryOverrides() => Chem.Reset();
    }
}
