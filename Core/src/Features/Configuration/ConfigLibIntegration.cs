using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Photocore.Configuration
{
    // Optional GUI front-end for photocore.json via the third-party ConfigLib mod
    // (github.com/maltiez2/vsmod_configlib). Soft dependency: IsPresent touches no ConfigLib types, so
    // it's always safe to call. Register is the only method referencing ConfigLib.* types, and callers
    // must only invoke it after IsPresent confirms the mod is actually loaded — the .NET JIT compiles
    // method bodies lazily on first call, so a player without ConfigLib installed never triggers a
    // type load from this class.
    internal static class ConfigLibIntegration
    {
        internal static bool IsPresent(ICoreAPI api) => api.ModLoader.GetMod("configlib") != null;

        // ConfigLib's own ModSystem directly implements IConfigProvider, so no separate registry/
        // service-locator step is needed. Subscribes to ConfigWindowClosed (fires once when the player
        // closes the settings GUI, not per keystroke/slider-drag) to reload photocore.json — ConfigLib
        // has already written the player's edits into that file via its "file" link mode
        // (configlib-patches.json), so reapplying is just re-running the same load path collodion
        // already uses at startup.
        internal static void Register(ICoreClientAPI capi, PhotocoreModSystem owner)
        {
            if (capi.ModLoader.GetModSystem<ConfigLib.ConfigLibModSystem>() is not ConfigLib.IConfigProvider provider) return;

            provider.ConfigWindowClosed += () => ReapplyConfig(capi, owner);
        }

        private static void ReapplyConfig(ICoreClientAPI capi, PhotocoreModSystem owner)
        {
            PhotocoreConfig reloaded = ConfigLifecycle.LoadOrCreate(capi, PhotocoreModSystem.ConfigFileName);

            // These are server-authoritative in real multiplayer (not singleplayer/hosting): the value
            // the server sent at join time wins over whatever this player's own local file now says, so
            // a reload here can't silently un-apply that authority mid-session.
            if (!capi.IsSinglePlayer)
            {
                if (owner.CameraCaptureBridge.ServerPhotoCaptureMaxDimensionOverride is int maxDimension)
                {
                    reloaded.Viewfinder.PhotoCaptureMaxDimension = maxDimension;
                }

                if (owner.CameraCaptureBridge.ServerApplyFinishingEffectsOverride is bool applyFinishing)
                {
                    reloaded.Viewfinder.ApplyFinishingEffects = applyFinishing;
                }

                if (owner.CameraCaptureBridge.ServerPhotoSeenPingIntervalSecondsOverride is int pingInterval)
                {
                    reloaded.PhotoSync.PhotoSeenPingIntervalSeconds = pingInterval;
                }
            }

            owner.ApplyConfig(reloaded);
        }
    }
}
