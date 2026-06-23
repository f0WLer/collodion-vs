using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Photochemistry.ImageEffects
{
    // .collodion effects/effect command behavior and persistence orchestration.
    internal static class ImageEffectsCommandHandler
    {
        // Handles .collodion effects subcommands and persists any config mutations to the wetplate profile.
        private const string Profile = "wetplate";

        internal static void HandleEffectsCommand(ICoreClientAPI capi, CmdArgs args)
        {
            ImageEffectsConfig Load() => ImageEffectsProfileService.TryLoadProfile(Profile, capi) ?? new ImageEffectsConfig();

            string param = args.PopWord();

            if (string.IsNullOrEmpty(param) || param.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsConfig cfg = Load();
                capi.ShowChatMessage($"Collodion photo effects: enabled={cfg.Enabled}");
                capi.ShowChatMessage($"  vignette={cfg.Vignette:0.00} (radius={cfg.VignetteRadius:0.00}) skyblowout={cfg.SkyBlowout:0.00} grain={cfg.Grain:0.00}");
                capi.ShowChatMessage($"  halation={cfg.Halation:0.00} lensaberration={cfg.LensAberration:0.00}");
                capi.ShowChatMessage($"  realism: imperfection={cfg.Imperfection:0.00} skyuneven={cfg.SkyUnevenness:0.00} skytop={cfg.SkyTopFraction:0.00} edgewarmth={cfg.EdgeWarmth:0.00}");
                capi.ShowChatMessage($"  dust={cfg.DustCount} (opacity={cfg.DustOpacity:0.00})");
                capi.ShowChatMessage($"  scratches={cfg.ScratchCount} (opacity={cfg.ScratchOpacity:0.00})");
                capi.ShowChatMessage($"  dynamic={cfg.DynamicEnabled} dynamicscale={cfg.DynamicScale:0.00}");
                capi.ShowChatMessage("Usage: .collodion effects <show|enable|disable|reset|set> [param] [value]");
                return;
            }

            if (param.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsProfileService.SaveProfile(Profile, new ImageEffectsConfig());
                capi.ShowChatMessage("photochemistry: effects reset to defaults");
                return;
            }

            if (param.Equals("enable", StringComparison.OrdinalIgnoreCase) || param.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                bool enable = param.Equals("enable", StringComparison.OrdinalIgnoreCase);
                ImageEffectsConfig cfg = Load();
                cfg.Enabled = enable;
                ImageEffectsProfileService.SaveProfile(Profile, cfg);
                capi.ShowChatMessage(enable ? "photochemistry: effects enabled" : "photochemistry: effects disabled");
                return;
            }

            if (param.Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                string prop = args.PopWord();
                string valStr = args.PopWord();

                if (string.IsNullOrEmpty(prop) || string.IsNullOrEmpty(valStr))
                {
                    capi.ShowChatMessage("usage: .collodion effects set <property> <value>");
                    capi.ShowChatMessage("Properties: vignette, vignetteradius, skyblowout, grain, imperfection, skyunevenness, skytopfraction, edgewarmth, dust, dustopacity, scratches, scratchopacity, dynamic, dynamicscale, halation, halationthreshold, halationradius, halationtint, lensaberration, lensaberrationstart, lensaberrationsigma");
                    return;
                }

                ImageEffectsConfig cfg = Load();

                if (!ImageEffectsCommandPropertyMap.TryApply(prop, valStr, cfg, out string? setError))
                {
                    capi.ShowChatMessage(setError ?? "photochemistry: failed to set effect property");
                    return;
                }

                cfg.ClampInPlace();
                ImageEffectsProfileService.SaveProfile(Profile, cfg);
                capi.ShowChatMessage($"photochemistry: set {prop} = {valStr}");
                capi.ShowChatMessage("Note: effects apply to newly taken photos. Use .collodion clearcache to reload existing photos.");
                return;
            }

            capi.ShowChatMessage("usage: .collodion effects <show|enable|disable|reset|set>");
        }
    }
}
