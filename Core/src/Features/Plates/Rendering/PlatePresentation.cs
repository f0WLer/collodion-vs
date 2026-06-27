using Vintagestory.API.Common;
using Photocore.Exposure;

namespace Photocore.Plates.Rendering
{
    /// <summary>
    /// The physical render model for a developed plate's image: a silver-on-glass density map read as a
    /// positive over a black backing (glass ambrotype), or an opaque reflective positive on a paper base.
    /// Selected by the item's <c>plateMedium</c> attribute.
    /// </summary>
    internal enum PresentationMedium
    {
        GlassPlate,
        PaperPrint,
    }

    /// <summary>
    /// A developed plate's final look: the metallic deposit colour, the density-map response, and the
    /// physical <see cref="PresentationMedium"/>. At runtime <see cref="Resolve(ItemStack)"/> builds this
    /// from the item's medium plus the chemistry's tuned tone in the profile registry.
    /// The static <see cref="GlassIodide"/>/<see cref="GlassBromide"/>/<see cref="PaperPrint"/> instances
    /// are the hardcoded tone defaults that <see cref="ChemistryProfileSeeder"/> seeds a fresh profile from.
    /// </summary>
    internal readonly struct PlatePresentation
    {
        /// <summary>Metallic deposit colour applied to the developed image (per chemistry).</summary>
        internal readonly byte DepositR;
        internal readonly byte DepositG;
        internal readonly byte DepositB;

        /// <summary>Physical medium the silver image lives on. Drives how it is composited for viewing.</summary>
        internal readonly PresentationMedium Medium;

        /// <summary>
        /// Tonal-response exponent applied to the silver density (alpha = luminance^DensityGamma).
        /// &lt;1 lifts shadow/midtone silver so the over-black positive (framed ambrotype) keeps
        /// detail instead of crushing to black; 1.0 is a linear, un-lifted response.
        /// </summary>
        internal readonly float DensityGamma;

        internal PlatePresentation(byte depositR, byte depositG, byte depositB, PresentationMedium medium, float densityGamma)
        {
            DepositR = depositR;
            DepositG = depositG;
            DepositB = depositB;
            Medium = medium;
            DensityGamma = densityGamma;
        }

        /// <summary>
        /// Wet-plate collodion (iodide): warm silver on glass, viewed as an ambrotype over a black
        /// backing. Also the glass-plate default for any unrecognised chemistry.
        /// </summary>
        internal static readonly PlatePresentation GlassIodide =
            new PlatePresentation(213, 208, 197, PresentationMedium.GlassPlate, densityGamma: 0.60f);

        /// <summary>
        /// Gelatin dry plate (bromide): a cooler, cleaner neutral silver than the warm wet-plate look,
        /// with slightly less shadow lift (higher gamma) so the rich maximum density reads as deeper blacks.
        /// </summary>
        internal static readonly PlatePresentation GlassBromide =
            new PlatePresentation(196, 201, 206, PresentationMedium.GlassPlate, densityGamma: 0.68f);

        /// <summary>
        /// Salted paper print (chloride): an opaque, reflective positive — warm reddish-brown iron/silver
        /// deposit composited over the paper base, rather than the silver-over-black glass model.
        /// </summary>
        internal static readonly PlatePresentation PaperPrint =
            new PlatePresentation(92, 56, 42, PresentationMedium.PaperPrint, densityGamma: 0.85f);

        /// <summary>The hardcoded seed tone for a chemistry — used by the chemistry-profile seeder to populate
        /// a fresh config's Presentation section. Runtime resolution reads the (possibly tuned) config instead.</summary>
        internal static PlatePresentation SeedFor(string? chemistry)
        {
            if (string.Equals(chemistry, "bromide", System.StringComparison.OrdinalIgnoreCase)) return GlassBromide;
            if (string.Equals(chemistry, "chloride", System.StringComparison.OrdinalIgnoreCase)) return PaperPrint;
            return GlassIodide;
        }

        /// <summary>Maps an itemtype <c>plateMedium</c> attribute to the physical render model. The medium is a
        /// property of the item (a paper sheet vs a glass plate), so it stays itemtype-driven; only the tone is data.</summary>
        internal static PresentationMedium ResolveMedium(string? medium) =>
            string.Equals(medium, "paperprint", System.StringComparison.OrdinalIgnoreCase)
                ? PresentationMedium.PaperPrint : PresentationMedium.GlassPlate;

        /// <summary>Resolves the presentation for a plate stack: the render model from its itemtype's
        /// <c>plateMedium</c> attribute, and the developed tone (deposit colour + density gamma) from the
        /// chemistry's profile in the shared registry — so the look is config-driven, not hardcoded.</summary>
        internal static PlatePresentation Resolve(ItemStack? stack)
        {
            PresentationMedium medium = ResolveMedium(stack?.Collectible?.Attributes?["plateMedium"]?.AsString(null));
            PresentationSettings tone = ChemistryProfileRegistry.Instance.Get(PlateAttributes.GetChemistry(stack)).Presentation;
            return new PlatePresentation(ToByte(tone.DepositR), ToByte(tone.DepositG), ToByte(tone.DepositB), medium, tone.DensityGamma);
        }

        private static byte ToByte(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
    }
}
