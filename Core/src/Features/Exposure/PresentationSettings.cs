namespace Photochemistry.Exposure
{
    // Silver/print tone per chemistry — data, not hardcoded. Physical render model (glass vs paper) is decided by plateMedium.
    internal sealed class PresentationSettings
    {
        public int DepositR { get; set; } = 213;
        public int DepositG { get; set; } = 208;
        public int DepositB { get; set; } = 197;
        public float DensityGamma { get; set; } = 0.60f;
    }
}
