namespace Photochemistry.Exposure
{
    // Chem is swapped by the owner when the active chemistry changes; NaN parameters inherit the process-profile default.
    internal sealed class ExposurePhysicsConfig
    {
        public ChemistryOverrides Chem { get; set; } = new();

        public bool Linearize       => Chem.Linearize;
        public bool SpectralWeights => Chem.SpectralWeights;
        public bool HDCurve         => Chem.HDCurve;
        public bool Normalize       => Chem.Normalize;
        public bool LogAccumulation => Chem.LogAccumulation;

        public float EffectiveDevStrength(in EmulsionProfile p)  => float.IsNaN(Chem.DevStrength)  ? p.DevelopmentStrength  : Chem.DevStrength;
        public float EffectiveHDGamma(in EmulsionProfile p)      => float.IsNaN(Chem.HDGamma)      ? p.HDGamma             : Chem.HDGamma;
        public float EffectiveRedSens(in EmulsionProfile p)      => float.IsNaN(Chem.RedSens)      ? p.RedSensitivity      : Chem.RedSens;
        public float EffectiveGreenSens(in EmulsionProfile p)    => float.IsNaN(Chem.GreenSens)    ? p.GreenSensitivity    : Chem.GreenSens;
        public float EffectiveBlueSens(in EmulsionProfile p)     => float.IsNaN(Chem.BlueSens)     ? p.BlueSensitivity     : Chem.BlueSens;
        public float EffectiveInertia(in EmulsionProfile p)      => float.IsNaN(Chem.Inertia)      ? p.InertiaPoint        : Chem.Inertia;
        public float EffectiveReciprocity(in EmulsionProfile p)  => float.IsNaN(Chem.Reciprocity)  ? p.ReciprocityExponent : Chem.Reciprocity;
        public float EffectiveExposureGain(in EmulsionProfile p) => float.IsNaN(Chem.ExposureGain) ? p.ExposureGain        : Chem.ExposureGain;

        public void ApplyPhysics(GpuExposureAccumulator buf)
        {
            buf.LinearizeInput              = Chem.Linearize;
            buf.ApplySpectralWeights        = Chem.SpectralWeights;
            buf.ApplyHDCurve                = Chem.HDCurve;
            buf.NormalizeByActualFrameCount = Chem.Normalize;
            buf.UseLogAccumulation          = Chem.LogAccumulation;
        }

        public void Apply(GpuExposureAccumulator buf, in EmulsionProfile process)
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

        public bool SetPhysics(string flag, bool value)
        {
            switch (flag)
            {
                case "linearize": Chem.Linearize       = value; break;
                case "spectral":  Chem.SpectralWeights = value; break;
                case "hdcurve":   Chem.HDCurve         = value; break;
                case "normalize": Chem.Normalize       = value; break;
                case "logaccum":  Chem.LogAccumulation = value; break;
                default: return false;
            }
            return true;
        }

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

        // Resets in place (not a new instance) so the owner's reference to Chem stays valid.
        public void ResetChemistryOverrides() => Chem.Reset();
    }
}
