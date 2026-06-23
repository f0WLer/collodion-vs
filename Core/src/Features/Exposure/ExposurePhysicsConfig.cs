namespace Photochemistry.Exposure
{
    /// <summary>
    /// Applies a chemistry's exposure settings to a <see cref="GpuExposureAccumulator"/>. Everything that
    /// varies per chemistry — the physics emulation flags, the per-parameter overrides (NaN = use the
    /// process-profile default), and the timing — lives on <see cref="Chem"/>; this type just resolves and
    /// copies them onto a buffer. The owner swaps <see cref="Chem"/> when the active chemistry changes.
    /// </summary>
    internal sealed class ExposurePhysicsConfig
    {
        // The active chemistry's settings (flags + overrides + timing). Never null.
        public ChemistryOverrides Chem { get; set; } = new();

        // Read passthroughs for the dialog's flag switches.
        public bool Linearize       => Chem.Linearize;
        public bool SpectralWeights => Chem.SpectralWeights;
        public bool HDCurve         => Chem.HDCurve;
        public bool Normalize       => Chem.Normalize;
        public bool LogAccumulation => Chem.LogAccumulation;

        public float EffectiveDevStrength(in PlateProcessProfile p)  => float.IsNaN(Chem.DevStrength)  ? p.DevelopmentStrength  : Chem.DevStrength;
        public float EffectiveHDGamma(in PlateProcessProfile p)      => float.IsNaN(Chem.HDGamma)      ? p.HDGamma             : Chem.HDGamma;
        public float EffectiveRedSens(in PlateProcessProfile p)      => float.IsNaN(Chem.RedSens)      ? p.RedSensitivity      : Chem.RedSens;
        public float EffectiveGreenSens(in PlateProcessProfile p)    => float.IsNaN(Chem.GreenSens)    ? p.GreenSensitivity    : Chem.GreenSens;
        public float EffectiveBlueSens(in PlateProcessProfile p)     => float.IsNaN(Chem.BlueSens)     ? p.BlueSensitivity     : Chem.BlueSens;
        public float EffectiveInertia(in PlateProcessProfile p)      => float.IsNaN(Chem.Inertia)      ? p.InertiaPoint        : Chem.Inertia;
        public float EffectiveReciprocity(in PlateProcessProfile p)  => float.IsNaN(Chem.Reciprocity)  ? p.ReciprocityExponent : Chem.Reciprocity;
        public float EffectiveExposureGain(in PlateProcessProfile p) => float.IsNaN(Chem.ExposureGain) ? p.ExposureGain        : Chem.ExposureGain;

        // Copies the active chemistry's physics flags onto a buffer.
        public void ApplyPhysics(GpuExposureAccumulator buf)
        {
            buf.LinearizeInput              = Chem.Linearize;
            buf.ApplySpectralWeights        = Chem.SpectralWeights;
            buf.ApplyHDCurve                = Chem.HDCurve;
            buf.NormalizeByActualFrameCount = Chem.Normalize;
            buf.UseLogAccumulation          = Chem.LogAccumulation;
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
                case "linearize": Chem.Linearize       = value; break;
                case "spectral":  Chem.SpectralWeights = value; break;
                case "hdcurve":   Chem.HDCurve         = value; break;
                case "normalize": Chem.Normalize       = value; break;
                case "logaccum":  Chem.LogAccumulation = value; break;
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
