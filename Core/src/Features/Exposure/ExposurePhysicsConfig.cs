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

        // NaN means "use the active process profile's default".
        public float DevStrength  = float.NaN;
        public float HDGamma      = float.NaN;
        public float RedSens      = float.NaN;
        public float GreenSens    = float.NaN;
        public float BlueSens     = float.NaN;
        public float Inertia      = float.NaN;
        public float Reciprocity  = float.NaN;
        public float ExposureGain = float.NaN;

        public float EffectiveDevStrength(in PlateProcessProfile p)  => float.IsNaN(DevStrength)  ? p.DevelopmentStrength  : DevStrength;
        public float EffectiveHDGamma(in PlateProcessProfile p)      => float.IsNaN(HDGamma)      ? p.HDGamma             : HDGamma;
        public float EffectiveRedSens(in PlateProcessProfile p)      => float.IsNaN(RedSens)      ? p.RedSensitivity      : RedSens;
        public float EffectiveGreenSens(in PlateProcessProfile p)    => float.IsNaN(GreenSens)    ? p.GreenSensitivity    : GreenSens;
        public float EffectiveBlueSens(in PlateProcessProfile p)     => float.IsNaN(BlueSens)     ? p.BlueSensitivity     : BlueSens;
        public float EffectiveInertia(in PlateProcessProfile p)      => float.IsNaN(Inertia)      ? p.InertiaPoint        : Inertia;
        public float EffectiveReciprocity(in PlateProcessProfile p)  => float.IsNaN(Reciprocity)  ? p.ReciprocityExponent : Reciprocity;
        public float EffectiveExposureGain(in PlateProcessProfile p) => float.IsNaN(ExposureGain) ? p.ExposureGain        : ExposureGain;

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

        // Sets a named chemistry override. Returns false when the name is unrecognised.
        public bool SetChemistry(string param, float value)
        {
            switch (param)
            {
                case "devstrength": DevStrength = value; break;
                case "hdgamma":     HDGamma     = value; break;
                case "redsens":     RedSens     = value; break;
                case "greensens":   GreenSens   = value; break;
                case "bluesens":    BlueSens    = value; break;
                case "inertia":     Inertia     = value; break;
                case "reciprocity":  Reciprocity  = value; break;
                case "exposuregain": ExposureGain = value; break;
                default: return false;
            }
            return true;
        }

        // Clears all chemistry overrides, restoring process-profile defaults.
        public void ResetChemistryOverrides()
        {
            DevStrength = HDGamma = RedSens = GreenSens = BlueSens = Inertia = Reciprocity = ExposureGain = float.NaN;
        }
    }
}
