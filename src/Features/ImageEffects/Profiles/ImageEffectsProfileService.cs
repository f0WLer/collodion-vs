using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Collodion.AdminTooling;

namespace Collodion.ImageEffects
{
    // Named effects profile lookup, fallback, and runtime snapshot behavior.
    internal static class ImageEffectsProfileService
    {
        // Gets bundled default profile location inside mod assets.
        internal static AssetLocation GetBundledProfileAssetLocation(string profileName)
        {
            string trimmed = (profileName ?? string.Empty).Trim().ToLowerInvariant();
            return new AssetLocation("collodion", $"config/effects/{trimmed}.json");
        }

        internal static bool ProfileExists(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return false;
            return File.Exists(GetProfilePath(profileName.Trim()));
        }

        internal static bool BundledProfileExists(ICoreClientAPI capi, string profileName)
        {
            if (capi == null || string.IsNullOrWhiteSpace(profileName)) return false;
            AssetLocation jsonLocation = GetBundledProfileAssetLocation(profileName.Trim());
            return capi.Assets.TryGet(jsonLocation, loadAsset: true) != null;
        }

        internal static bool TrySeedFromBundledAsset(ICoreClientAPI capi, string profileName, out string modDataPath)
        {
            modDataPath = string.Empty;
            if (capi == null || string.IsNullOrWhiteSpace(profileName)) return false;

            string trimmed = profileName.Trim();
            if (ProfileExists(trimmed)) return false;

            ImageEffectsConfig? bundledCfg = TryLoadBundledProfile(capi, trimmed);
            if (bundledCfg == null) return false;

            try
            {
                SaveProfile(trimmed, bundledCfg);
                modDataPath = GetProfilePath(trimmed);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(capi.Logger, "failed to seed ModData effects profile '{0}' from bundled assets: {1}", trimmed, ex.Message);
                return false;
            }
        }

        // Loads bundled profile from mod assets.
        private static ImageEffectsConfig? TryLoadBundledProfile(ICoreClientAPI capi, string profileName)
        {
            if (capi == null || string.IsNullOrWhiteSpace(profileName)) return null;

            try
            {
                AssetLocation jsonLocation = GetBundledProfileAssetLocation(profileName);
                IAsset? asset = capi.Assets.TryGet(jsonLocation, loadAsset: true);

                if (asset == null) return null;

                ImageEffectsConfig? cfg = JsonConvert.DeserializeObject<ImageEffectsConfig>(asset.ToText());
                cfg?.ClampInPlace();
                return cfg;
            }
            catch (Exception ex)
            {
                Log.Warn(capi.Logger, "failed to parse bundled effects profile '{0}' at '{1}': {2}", profileName, GetBundledProfileAssetLocation(profileName), ex.Message);
                return null;
            }
        }

        internal static ImageEffectsConfig? TryLoadProfile(string profileName, ICoreClientAPI? capi = null)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return null;

            string trimmed = profileName.Trim();

            string path = GetProfilePath(trimmed);
            if (File.Exists(path))
            {
                try
                {
                    ImageEffectsConfig? cfg = JsonConvert.DeserializeObject<ImageEffectsConfig>(File.ReadAllText(path));
                    cfg?.ClampInPlace();
                    return cfg;
                }
                catch (Exception ex)
                {
                    Log.Warn(capi?.Logger, "failed to parse ModData effects profile '{0}' at '{1}': {2}; falling back to bundled profile", trimmed, path, ex.Message);
                    // Fall back to bundled profile below.
                }
            }

            if (capi == null) return null;

            ImageEffectsConfig? bundledCfg = TryLoadBundledProfile(capi, trimmed);
            if (bundledCfg == null) return null;

            // Self-heal missing ModData profile files when bundled defaults are used at runtime.
            if (!ProfileExists(trimmed))
            {
                try
                {
                    SaveProfile(trimmed, bundledCfg);
                }
                catch (Exception ex)
                {
                    Log.Warn(capi.Logger, "failed to write self-healed ModData effects profile '{0}': {1}", trimmed, ex.Message);
                }
            }

            return bundledCfg;
        }

        // Loads active config from mod system, ensuring an effects profile exists and is clamped.
        internal static ImageEffectsConfig LoadOrCreate(ICoreClientAPI capi)
        {
            CollodionModSystem? modSys = CollodionConfigAccess.ResolveClientModSystem(capi);
            if (modSys == null)
            {
                return CreateRuntimeSnapshot(null);
            }

            CollodionConfig cfg = modSys.GetOrLoadClientConfig(capi);
            bool dirty = false;

            if (cfg.Effects == null)
            {
                cfg.Effects = new ImageEffectsConfig();
                dirty = true;
            }

            cfg.Effects.ClampInPlace();

            if (dirty)
            {
                modSys.SaveClientConfig(capi);
            }

            return CreateRuntimeSnapshot(cfg.Effects);
        }

        // Clones and clamps an effects profile for runtime use so hot paths can treat it as immutable.
        internal static ImageEffectsConfig CreateRuntimeSnapshot(ImageEffectsConfig? cfg)
        {
            ImageEffectsConfig snapshot = cfg?.Clone() ?? new ImageEffectsConfig();
            snapshot.ClampInPlace();
            return snapshot;
        }

        internal static string GetProfilePath(string? name = null)
        {
            string file = string.IsNullOrWhiteSpace(name) ? "effects-tuning" : name;
            return Path.Combine(GamePaths.DataPath, "ModData", "collodion", $"{file}.json");
        }

        private static void SaveProfile(string? name, ImageEffectsConfig cfg)
        {
            string path = GetProfilePath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }
    }
}