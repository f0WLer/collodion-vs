using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Collodion.AdminTooling;

namespace Collodion.ImageEffects
{
    // .collodion effects/effect command behavior and persistence orchestration.
    internal static class ImageEffectsCommandHandler
    {
        // Handles .collodion effects subcommands and persists any config mutations.
        internal static void HandleEffectsCommand(ICoreClientAPI capi, CollodionConfig rootCfg, CmdArgs args, Action<CollodionConfig> persistConfig)
        {
            rootCfg.Effects ??= new ImageEffectsConfig();

            string param = args.PopWord();

            if (string.IsNullOrEmpty(param) || param.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsConfig cfg = rootCfg.Effects;
                capi.ShowChatMessage($"Collodion photo effects: enabled={cfg.Enabled}");
                capi.ShowChatMessage($"  greyscale={cfg.Greyscale} preGray RGB=({cfg.PreGrayRed:0.00}, {cfg.PreGrayGreen:0.00}, {cfg.PreGrayBlue:0.00})");
                capi.ShowChatMessage($"  sepia={cfg.SepiaStrength:0.00} contrast={cfg.Contrast:0.00} brightness={cfg.Brightness:0.00}");
                capi.ShowChatMessage($"  curve: shoulder={cfg.HighlightShoulder:0.00} threshold={cfg.HighlightThreshold:0.00} shadowfloor={cfg.ShadowFloor:0.00} contraststart={cfg.ContrastStart:0.00}");
                capi.ShowChatMessage($"  vignette={cfg.Vignette:0.00} skyblowout={cfg.SkyBlowout:0.00} grain={cfg.Grain:0.00}");
                capi.ShowChatMessage($"  realism: imperfection={cfg.Imperfection:0.00} microblur={cfg.MicroBlur:0.00} skyuneven={cfg.SkyUnevenness:0.00} skytop={cfg.SkyTopFraction:0.00} edgewarmth={cfg.EdgeWarmth:0.00}");
                capi.ShowChatMessage($"  dust={cfg.DustCount} (opacity={cfg.DustOpacity:0.00})");
                capi.ShowChatMessage($"  scratches={cfg.ScratchCount} (opacity={cfg.ScratchOpacity:0.00})");
                capi.ShowChatMessage($"  dynamic={cfg.DynamicEnabled} dynamicscale={cfg.DynamicScale:0.00}");
                capi.ShowChatMessage("Usage: .collodion effects <show|enable|disable|reset|preset|set> [param] [value]");
                return;
            }

            if (param.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                rootCfg.Effects = new ImageEffectsConfig();
                persistConfig(rootCfg);
                capi.ShowChatMessage("Collodion: effects reset to defaults");
                return;
            }

            if (param.Equals("enable", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsConfig cfg = rootCfg.Effects;
                cfg.Enabled = true;
                persistConfig(rootCfg);
                capi.ShowChatMessage("Collodion: effects enabled");
                return;
            }

            if (param.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsConfig cfg = rootCfg.Effects;
                cfg.Enabled = false;
                persistConfig(rootCfg);
                capi.ShowChatMessage("Collodion: effects disabled");
                return;
            }

            if (param.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                string which = args.PopWord();
                if (string.IsNullOrEmpty(which))
                {
                    capi.ShowChatMessage("usage: .collodion effects preset <indoor|outdoor>");
                    return;
                }

                bool isIndoor = which.Equals("indoor", StringComparison.OrdinalIgnoreCase);
                bool isOutdoor = which.Equals("outdoor", StringComparison.OrdinalIgnoreCase);

                if (!isIndoor && !isOutdoor)
                {
                    capi.ShowChatMessage("Collodion: preset must be 'indoor' or 'outdoor'");
                    return;
                }

                ImageEffectsConfig? preset = isIndoor ? rootCfg.EffectsPresetIndoor : rootCfg.EffectsPresetOutdoor;

                if (preset == null)
                {
                    // Seed from current active config
                    ImageEffectsConfig current = rootCfg.Effects ?? new ImageEffectsConfig();
                    preset = current.Clone();
                    if (isIndoor) rootCfg.EffectsPresetIndoor = preset.Clone();
                    else rootCfg.EffectsPresetOutdoor = preset.Clone();
                }

                preset.ClampInPlace();

                try
                {
                    // Activate preset by writing to the active config
                    rootCfg.Effects = preset.Clone();
                    persistConfig(rootCfg);
                    capi.ShowChatMessage($"Collodion: preset '{which}' activated");
                }
                catch
                {
                    capi.ShowChatMessage("Collodion: failed to activate preset");
                }
                return;
            }

            if (param.Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                string prop = args.PopWord();
                string valStr = args.PopWord();

                if (string.IsNullOrEmpty(prop) || string.IsNullOrEmpty(valStr))
                {
                    capi.ShowChatMessage("usage: .collodion effects set <property> <value>");
                    capi.ShowChatMessage("Properties: greyscale, pregrayred, pregraygreen, pregrayblue, sepia, contrast, brightness, shadowfloor, contraststart, highlightshoulder, highlightthreshold, vignette, vignetteradius, skyblowout, grain, imperfection, microblur, skyunevenness, skytopfraction, edgewarmth, dust, dustopacity, scratches, scratchopacity, dynamic, dynamicscale, halation, halationthreshold, halationradius, halationtint, lensaberration, lensaberrationstart, lensaberrationsigma, curveredtoe, curveredmid, curveredshoulder, curvegreentoe, curvegreenmid, curvegreenshoulder, curvebluetoe, curvebluemid, curveblueshoulder");
                    return;
                }

                ImageEffectsConfig cfg = rootCfg.Effects;

                if (!ImageEffectsCommandPropertyMap.TryApply(prop, valStr, cfg, out string? setError))
                {
                    capi.ShowChatMessage(setError ?? "Collodion: failed to set effect property");
                    return;
                }

                persistConfig(rootCfg);
                capi.ShowChatMessage($"Collodion: set {prop} = {valStr}");
                capi.ShowChatMessage("Note: effects apply to newly taken photos. Use .collodion clearcache to reload existing photos.");
                return;
            }

            capi.ShowChatMessage("usage: .collodion effects <show|enable|disable|reset|preset|set>");
        }
    }
}