using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Vintagestory.API.Config;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private void HandleWetplateEffectsCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            var rootCfg = GetOrLoadClientConfig(ClientApi);
            rootCfg.Effects ??= new WetplateEffectsConfig();

            string param = args.PopWord();

            if (string.IsNullOrEmpty(param) || param.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = rootCfg.Effects;
                ClientApi.ShowChatMessage($"Wetplate effects: enabled={cfg.Enabled}");
                ClientApi.ShowChatMessage($"  greyscale={cfg.Greyscale} preGray RGB=({cfg.PreGrayRed:0.00}, {cfg.PreGrayGreen:0.00}, {cfg.PreGrayBlue:0.00})");
                ClientApi.ShowChatMessage($"  sepia={cfg.SepiaStrength:0.00} contrast={cfg.Contrast:0.00} brightness={cfg.Brightness:0.00}");
                ClientApi.ShowChatMessage($"  curve: shoulder={cfg.HighlightShoulder:0.00} threshold={cfg.HighlightThreshold:0.00} shadowfloor={cfg.ShadowFloor:0.00} contraststart={cfg.ContrastStart:0.00}");
                ClientApi.ShowChatMessage($"  vignette={cfg.Vignette:0.00} skyblowout={cfg.SkyBlowout:0.00} grain={cfg.Grain:0.00}");
                ClientApi.ShowChatMessage($"  realism: imperfection={cfg.Imperfection:0.00} microblur={cfg.MicroBlur:0.00} skyuneven={cfg.SkyUnevenness:0.00} skytop={cfg.SkyTopFraction:0.00} edgewarmth={cfg.EdgeWarmth:0.00}");
                ClientApi.ShowChatMessage($"  dust={cfg.DustCount} (opacity={cfg.DustOpacity:0.00})");
                ClientApi.ShowChatMessage($"  scratches={cfg.ScratchCount} (opacity={cfg.ScratchOpacity:0.00})");
                ClientApi.ShowChatMessage($"  dynamic={cfg.DynamicEnabled} dynamicscale={cfg.DynamicScale:0.00}");
                ClientApi.ShowChatMessage("Usage: .collodion effects <show|enable|disable|reset|preset|set> [param] [value]");
                return;
            }

            if (param.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                rootCfg.Effects = new WetplateEffectsConfig();
                rootCfg.Effects.ClampInPlace();
                SaveClientConfig(ClientApi);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage("Wetplate: effects reset to defaults");
                return;
            }

            if (param.Equals("enable", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = rootCfg.Effects;
                cfg.Enabled = true;
                cfg.ClampInPlace();
                SaveClientConfig(ClientApi);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage("Wetplate: effects enabled");
                return;
            }

            if (param.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = rootCfg.Effects;
                cfg.Enabled = false;
                cfg.ClampInPlace();
                SaveClientConfig(ClientApi);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage("Wetplate: effects disabled");
                return;
            }

            if (param.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                string which = args.PopWord();
                if (string.IsNullOrEmpty(which))
                {
                    ClientApi.ShowChatMessage("Wetplate: usage: .collodion effects preset <indoor|outdoor>");
                    return;
                }

                bool isIndoor = which.Equals("indoor", StringComparison.OrdinalIgnoreCase);
                bool isOutdoor = which.Equals("outdoor", StringComparison.OrdinalIgnoreCase);

                if (!isIndoor && !isOutdoor)
                {
                    ClientApi.ShowChatMessage("Wetplate: preset must be 'indoor' or 'outdoor'");
                    return;
                }

                WetplateEffectsConfig? preset = isIndoor ? rootCfg.EffectsPresetIndoor : rootCfg.EffectsPresetOutdoor;

                if (preset == null)
                {
                    // Seed from current active config
                    var current = rootCfg.Effects ?? new WetplateEffectsConfig();
                    preset = current.Clone();
                    if (isIndoor) rootCfg.EffectsPresetIndoor = preset.Clone();
                    else rootCfg.EffectsPresetOutdoor = preset.Clone();
                }

                preset.ClampInPlace();

                try
                {
                    // Activate preset by writing to the active config
                    rootCfg.Effects = preset.Clone();
                    rootCfg.Effects.ClampInPlace();
                    SaveClientConfig(ClientApi);
                    CaptureRenderer?.ReloadEffectsConfig();
                    ClientApi.ShowChatMessage($"Wetplate: preset '{which}' activated");
                }
                catch
                {
                    ClientApi.ShowChatMessage("Wetplate: failed to activate preset");
                }
                return;
            }

            if (param.Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                string prop = args.PopWord();
                string valStr = args.PopWord();

                if (string.IsNullOrEmpty(prop) || string.IsNullOrEmpty(valStr))
                {
                    ClientApi.ShowChatMessage("Wetplate: usage: .collodion effects set <property> <value>");
                    ClientApi.ShowChatMessage("Properties: greyscale, pregrayred, pregraygreen, pregrayblue, sepia, contrast, brightness, shadowfloor, contraststart, highlightshoulder, highlightthreshold, vignette, skyblowout, grain, imperfection, microblur, skyunevenness, skytopfraction, edgewarmth, dust, dustopacity, scratches, scratchopacity, dynamic, dynamicscale");
                    return;
                }

                var cfg = rootCfg.Effects;

                switch (prop.ToLowerInvariant())
                {
                    case "greyscale":
                    case "grayscale":
                    case "gray":
                    case "grey":
                        if (!bool.TryParse(valStr, out bool gs))
                        {
                            ClientApi.ShowChatMessage("Wetplate: greyscale value must be true/false");
                            return;
                        }
                        cfg.Greyscale = gs;
                        break;

                    case "pregrayred":
                    case "pregreyr":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float pgr))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.PreGrayRed = pgr;
                        break;

                    case "pregraygreen":
                    case "pregreyg":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float pgg))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.PreGrayGreen = pgg;
                        break;

                    case "pregrayblue":
                    case "pregreyb":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float pgb))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.PreGrayBlue = pgb;
                        break;

                    case "imperfection":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float imp))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Imperfection = imp;
                        break;

                    case "microblur":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float mb))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.MicroBlur = mb;
                        break;

                    case "skyunevenness":
                    case "skyuneven":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float su))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SkyUnevenness = su;
                        break;

                    case "skytopfraction":
                    case "skytop":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float st))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SkyTopFraction = st;
                        break;

                    case "edgewarmth":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float ew))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.EdgeWarmth = ew;
                        break;

                    case "sepia":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sep))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SepiaStrength = sep;
                        break;
                    case "contrast":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float con))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Contrast = con;
                        break;

                    case "highlightshoulder":
                    case "shoulder":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float hs))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.HighlightShoulder = hs;
                        break;

                    case "highlightthreshold":
                    case "threshold":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float ht))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.HighlightThreshold = ht;
                        break;
                    case "brightness":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float bri))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Brightness = bri;
                        break;

                    case "shadowfloor":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sf))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.ShadowFloor = sf;
                        break;

                    case "contraststart":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float cs))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.ContrastStart = cs;
                        break;
                    case "vignette":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float vig))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Vignette = vig;
                        break;

                    case "skyblowout":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sb))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SkyBlowout = sb;
                        break;
                    case "grain":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float gr))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Grain = gr;
                        break;
                    case "dust":
                        if (!int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dust))
                        {
                            ClientApi.ShowChatMessage("Wetplate: dust must be an integer");
                            return;
                        }
                        cfg.DustCount = dust;
                        break;
                    case "dustopacity":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float dop))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.DustOpacity = dop;
                        break;
                    case "scratches":
                        if (!int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scr))
                        {
                            ClientApi.ShowChatMessage("Wetplate: scratches must be an integer");
                            return;
                        }
                        cfg.ScratchCount = scr;
                        break;
                    case "scratchopacity":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sop))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.ScratchOpacity = sop;
                        break;
                    case "dynamic":
                    case "dynamicenabled":
                        if (!bool.TryParse(valStr, out bool dyn))
                        {
                            ClientApi.ShowChatMessage("Wetplate: dynamic value must be true/false");
                            return;
                        }
                        cfg.DynamicEnabled = dyn;
                        break;
                    case "dynamicscale":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float ds))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.DynamicScale = ds;
                        break;
                    default:
                        ClientApi.ShowChatMessage($"Wetplate: unknown property '{prop}'");
                        return;
                }

                cfg.ClampInPlace();
                SaveClientConfig(ClientApi);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage($"Wetplate: set {prop} = {valStr}");
                ClientApi.ShowChatMessage("Note: effects apply to newly taken photos. Use .collodion clearcache to reload existing photos.");
                return;
            }

            ClientApi.ShowChatMessage("Wetplate: usage: .collodion effects <show|enable|disable|reset|preset|set>");
        }

        private static readonly System.Text.RegularExpressions.Regex SafeProfileName =
            new(@"^[a-zA-Z0-9_\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string EffectsTuningProfilePathFor(string? name = null)
        {
            string file = string.IsNullOrWhiteSpace(name) ? "effects-tuning" : name;
            return Path.Combine(GamePaths.DataPath, "ModData", "collodion", $"{file}.json");
        }

        private void HandleEffectFieldCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            var rootCfg = GetOrLoadClientConfig(ClientApi);
            rootCfg.Effects ??= new WetplateEffectsConfig();

            string sub = args.PopWord();

            if (string.IsNullOrEmpty(sub))
            {
                ClientApi.ShowChatMessage("Usage: .collodion effect <FieldName> <value>");
                ClientApi.ShowChatMessage("       .collodion effect save [name]  — save to <name>.json (default: effects-tuning.json)");
                ClientApi.ShowChatMessage("       .collodion effect load [name]  — load from <name>.json (default: effects-tuning.json)");
                ClientApi.ShowChatMessage("Field names match WetplateEffectsConfig exactly (case-insensitive, exact match preferred).");
                return;
            }

            if (sub.Equals("save", StringComparison.OrdinalIgnoreCase))
            {
                string? profileName = args.PopWord();
                if (!string.IsNullOrWhiteSpace(profileName) && !SafeProfileName.IsMatch(profileName))
                {
                    ClientApi.ShowChatMessage("Effects save failed: profile name may only contain letters, digits, hyphens, and underscores.");
                    return;
                }
                string path = EffectsTuningProfilePathFor(profileName);
                var cfg = rootCfg.Effects;
                cfg.ClampInPlace();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                    ClientApi.ShowChatMessage($"Effects profile saved to: {path}");
                }
                catch (Exception ex)
                {
                    ClientApi.ShowChatMessage($"Effects save failed: {ex.Message}");
                }
                return;
            }

            if (sub.Equals("load", StringComparison.OrdinalIgnoreCase))
            {
                string? profileName = args.PopWord();
                if (!string.IsNullOrWhiteSpace(profileName) && !SafeProfileName.IsMatch(profileName))
                {
                    ClientApi.ShowChatMessage("Effects load failed: profile name may only contain letters, digits, hyphens, and underscores.");
                    return;
                }
                string path = EffectsTuningProfilePathFor(profileName);
                if (!File.Exists(path))
                {
                    ClientApi.ShowChatMessage($"No saved profile found at: {path}");
                    return;
                }
                try
                {
                    string text = File.ReadAllText(path);
                    var loaded = JsonConvert.DeserializeObject<WetplateEffectsConfig>(text);
                    if (loaded == null)
                    {
                        ClientApi.ShowChatMessage("Effects load failed: file parsed as null.");
                        return;
                    }
                    loaded.ClampInPlace();
                    rootCfg.Effects = loaded;
                    SaveClientConfig(ClientApi);
                    CaptureRenderer?.ReloadEffectsConfig();
                    ClientApi.ShowChatMessage($"Effects profile loaded from: {path}");
                }
                catch (Exception ex)
                {
                    ClientApi.ShowChatMessage($"Effects load failed: {ex.Message}");
                }
                return;
            }

            // sub is a field name; next arg is the value
            string fieldName = sub;
            string? valStr = args.PopWord();

            if (string.IsNullOrEmpty(valStr))
            {
                ClientApi.ShowChatMessage($"Usage: .collodion effect {fieldName} <value>");
                return;
            }

            var cfgType = typeof(WetplateEffectsConfig);
            FieldInfo? field = cfgType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

            if (field == null)
            {
                // Case-insensitive fallback
                var allFields = cfgType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var matches = new List<FieldInfo>();
                foreach (var f in allFields)
                {
                    if (string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                        matches.Add(f);
                }

                if (matches.Count == 0)
                {
                    ClientApi.ShowChatMessage($"Unknown field '{fieldName}'. Names match WetplateEffectsConfig public fields.");
                    return;
                }
                if (matches.Count > 1)
                {
                    var names = string.Join(", ", matches.ConvertAll(f => f.Name));
                    ClientApi.ShowChatMessage($"Ambiguous: multiple matches for '{fieldName}': {names}. Use exact casing.");
                    return;
                }
                field = matches[0];
            }

            var cfg2 = rootCfg.Effects;
            Type fieldType = field.FieldType;

            if (fieldType == typeof(float))
            {
                if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fval))
                {
                    ClientApi.ShowChatMessage($"'{field.Name}' is a float — value must be a number (use . not ,).");
                    return;
                }
                field.SetValue(cfg2, fval);
            }
            else if (fieldType == typeof(int))
            {
                if (!int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ival))
                {
                    ClientApi.ShowChatMessage($"'{field.Name}' is an int — value must be a whole number.");
                    return;
                }
                field.SetValue(cfg2, ival);
            }
            else if (fieldType == typeof(bool))
            {
                if (!bool.TryParse(valStr, out bool bval))
                {
                    ClientApi.ShowChatMessage($"'{field.Name}' is a bool — value must be true or false.");
                    return;
                }
                field.SetValue(cfg2, bval);
            }
            else
            {
                ClientApi.ShowChatMessage($"Field '{field.Name}' has unsupported type '{fieldType.Name}'.");
                return;
            }

            cfg2.ClampInPlace();
            SaveClientConfig(ClientApi);
            CaptureRenderer?.ReloadEffectsConfig();
            ClientApi.ShowChatMessage($"effect {field.Name} → {field.GetValue(cfg2)}");
        }
    }
}
