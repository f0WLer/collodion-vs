using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Photochemistry.Plates.Rendering
{
    using Photochemistry.Plates;

    /// <summary>
    /// How a developed plate's silver image is physically presented. Today only glass plates exist
    /// (collodion ambrotype: a silver-on-glass image read as a positive over a black backing).
    /// <see cref="PaperPrint"/> is reserved for a future paper process (silver chloride salt/albumen):
    /// an opaque, reflective positive on a paper base — which would need its own render branch rather
    /// than the silver-over-black model. Not branched on yet.
    /// </summary>
    internal enum PresentationMedium
    {
        GlassPlate,
        PaperPrint, // reserved: opaque reflective positive on paper
    }

    /// <summary>
    /// The single named home for a developed plate's final look, keyed by two axes: the physical
    /// <see cref="PresentationMedium"/> (glass density map vs opaque paper positive — decides the render
    /// model) and the chemistry (decides the silver image colour and contrast within a medium). A new
    /// process adds an instance here and a branch in <see cref="Resolve(string?, string?)"/> rather than
    /// touching the image processor.
    /// </summary>
    internal readonly struct PlatePresentation
    {
        /// <summary>Metallic silver deposit colour applied to the density map (warm silvery-gray).</summary>
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
        internal static readonly PlatePresentation Photochemistry =
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

        /// <summary>Resolves the final look from the medium name (itemtype <c>plateMedium</c> attribute) and
        /// the chemistry tag. Paper is its own medium (chloride); glass tone/contrast vary by chemistry.
        /// Null/empty/unknown values fall back to the warm iodide glass default, so existing items are unaffected.</summary>
        internal static PlatePresentation Resolve(string? medium, string? chemistry)
        {
            if (string.Equals(medium, "paperprint", System.StringComparison.OrdinalIgnoreCase)) return PaperPrint;
            if (string.Equals(chemistry, "bromide", System.StringComparison.OrdinalIgnoreCase)) return GlassBromide;
            return Photochemistry;
        }

        /// <summary>Resolves the presentation for a plate stack from its itemtype's <c>plateMedium</c> attribute
        /// and its per-stack chemistry tag.</summary>
        internal static PlatePresentation Resolve(ItemStack? stack) =>
            Resolve(stack?.Collectible?.Attributes?["plateMedium"]?.AsString(null), PlateAttributes.GetChemistry(stack));
    }
}
