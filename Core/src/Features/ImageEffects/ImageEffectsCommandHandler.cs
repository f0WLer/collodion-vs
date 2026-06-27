using Photocore.Exposure;
using Photocore.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Photocore.ImageEffects
{
    // .collodion effects command. Post-effects are now per-chemistry and live in the unified chemistry
    // profile, edited in the exposure-physics dialog (Save Profile persists them). This command is therefore
    // read-only: it reports each chemistry's current effects so they can be inspected outside the dialog.
    internal static class ImageEffectsCommandHandler
    {
        internal static void HandleEffectsCommand(ICoreClientAPI capi, CmdArgs args)
        {
            // Consume any subcommand/args so old "set/reset" muscle memory gets a clear message rather than silence.
            args.PopAll();

            capi.ShowChatMessage("Collodion post-effects are per-chemistry. Tune them in the exposure-physics dialog (Save Profile to persist).");

            foreach (string code in SensitizationRegistry.RegisteredChemistries())
            {
                ImageEffectsConfig fx = ChemistryProfileRegistry.Instance.Get(code).PostEffects;
                capi.ShowChatMessage(
                    $"  {code}: enabled={fx.Enabled} grain={fx.Grain:0.000} vignette={fx.Vignette:0.000} " +
                    $"halation={fx.Halation:0.000} skyblowout={fx.SkyBlowout:0.000} lensaberration={fx.LensAberration:0.000} " +
                    $"imperfection={fx.Imperfection:0.000} edgewarmth={fx.EdgeWarmth:0.000} dust={fx.DustCount} scratches={fx.ScratchCount}");
            }
        }
    }
}
