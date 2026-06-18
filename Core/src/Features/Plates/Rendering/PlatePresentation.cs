namespace Collodion.Plates.Rendering
{
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
    /// The single named home for a process's final presentation characteristics — the metallic
    /// deposit colour and the physical medium. Today there is exactly one instance
    /// (<see cref="Collodion"/>); a second process edits/adds an instance here rather than touching
    /// the image processor. This only locates the boundary; it is not threaded from the plate yet.
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
        /// Wet-plate collodion: warm silver on glass, viewed as an ambrotype over a black backing.
        /// </summary>
        internal static readonly PlatePresentation Collodion =
            new PlatePresentation(213, 208, 197, PresentationMedium.GlassPlate, densityGamma: 0.60f);
    }
}
