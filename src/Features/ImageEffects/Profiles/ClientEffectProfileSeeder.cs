using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion.ImageEffects
{
    // One-time client-side process-effects profile seeding and validation warnings.
    internal static class ClientEffectProfileSeeder
    {
        // Seeds bundled defaults and emits missing-profile warnings once per session.
        internal static bool TryPrepare(
            ICoreClientAPI capi,
            bool alreadyPrepared,
            ILogger? bestEffortLogger)
        {
            if (alreadyPrepared) return true;

            try
            {
                SeedWetplateProfile(capi);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(capi.Logger, "failed to prepare process effects profiles: {0}", ex.Message);
                Log.Warn(bestEffortLogger, "best-effort operation '{0}' failed: {1}: {2}", "prepare process effects profiles during assets load", ex.GetType().Name, ex.Message);
                return false;
            }
        }

        // Seeds the single wetplate profile from bundled assets if not already present in ModData.
        private static void SeedWetplateProfile(ICoreClientAPI capi)
        {
            const string profileName = "wetplate";
            if (ImageEffectsProfileService.TrySeedFromBundledAsset(capi, profileName, out string modDataPath))
            {
                Log.Notify(capi.Logger, "seeded default effects profile '{0}' to '{1}'", profileName, modDataPath);
            }
            else if (!ImageEffectsProfileService.ProfileExists(profileName) && !ImageEffectsProfileService.BundledProfileExists(capi, profileName))
            {
                string path = ImageEffectsProfileService.GetProfilePath(profileName);
                AssetLocation bundledPath = ImageEffectsProfileService.GetBundledProfileAssetLocation(profileName);
                Log.Warn(capi.Logger, "effects profile '{0}' not found in ModData ('{1}') or bundled assets ('{2}'); capture falls back to baseline effects", profileName, path, bundledPath);
            }
        }
    }
}