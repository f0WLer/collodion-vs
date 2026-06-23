namespace Photochemistry.Exposure
{
    /// <summary>
    /// The per-chemistry developed-image tone: the metallic deposit colour and the density-map response
    /// exponent. Persisted in the unified chemistry profile and read by the rendering layer, so the silver/
    /// print colour of a developed plate is data, not a hardcoded constant. The physical render model
    /// (glass density map vs opaque paper positive) is decided separately by the item's plateMedium.
    /// </summary>
    internal sealed class PresentationSettings
    {
        public int DepositR { get; set; } = 213;
        public int DepositG { get; set; } = 208;
        public int DepositB { get; set; } = 197;
        public float DensityGamma { get; set; } = 0.60f;
    }
}
