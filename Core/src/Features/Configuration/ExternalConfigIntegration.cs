using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Photocore.Configuration
{
    // Live reload of photocore.json when an optional in-game config-editor mod saves it. Two are
    // supported: ConfigLib (github.com/maltiez2/vsmod_configlib) and Integrated Mod Manager
    // (github.com/HugoCortell/VS_IMM). Both patch photocore.json directly (see
    // assets/<head>/config/configlib-patches.json and .../config/imm.json), so "reapply" for either
    // is just re-running the same load path used at startup.
    //
    // This deliberately references no types from either mod. Both publish a save signal on the
    // game's own event bus under a documented, domain/modid-scoped name, so listening needs no
    // assembly reference, no build flag, and no conditional compilation. Without the mod installed
    // the event is simply never pushed and these listeners never fire -- the whole soft-dependency
    // story for both integrations.
    internal static class ExternalConfigIntegration
    {
        // ConfigLibModSystem.ConfigSavedEvent is "configlib:{0}:config-saved", keyed by the domain
        // owning configlib-patches.json (ours). IMM's is "imm.{modId}", keyed by the *mod id* owning
        // config/imm.json instead (collodion or kosphotography) -- computed from owner.Mod.Info.ModID
        // per head rather than a constant. Both fire once per actual save, after the file is written.
        private const string ConfigLibSavedEvent = "configlib:photocore:config-saved";

        internal static void RegisterClient(ICoreClientAPI capi, PhotocoreModSystem owner)
        {
            capi.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) => OnClientConfigSaved(capi, owner),
                filterByEventName: ConfigLibSavedEvent);

            capi.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) => OnClientConfigSaved(capi, owner),
                filterByEventName: "imm." + owner.Mod.Info.ModID);
        }

        internal static void RegisterServer(ICoreServerAPI sapi, PhotocoreModSystem owner)
        {
            sapi.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) => OnServerConfigSaved(sapi, owner, data, isConfigLibPath: true),
                filterByEventName: ConfigLibSavedEvent);

            sapi.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) => OnServerConfigSaved(sapi, owner, data, isConfigLibPath: false),
                filterByEventName: "imm." + owner.Mod.Info.ModID);
        }

        private static void OnClientConfigSaved(ICoreClientAPI capi, PhotocoreModSystem owner)
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

        // Client-side capture reads the config above, but PlateProcessing/tray settings (cloth consumption,
        // pour durations, wet duration) are enforced server-side off the server ModSystem's own Config, so
        // the server has to reload too or those stay stale until a world reload.
        //
        // isConfigLibPath skips dedicated servers (ConfigLib's event races its own file write there, so a
        // reload here would load stale pre-save values -- dedicated servers just pick up ConfigLib changes
        // on restart instead) and re-checks privilege (ConfigLib relays every save unchecked). IMM already
        // guarantees both -- writes before pushing, checks Privilege.controlserver -- so its path skips both.
        private static void OnServerConfigSaved(ICoreServerAPI sapi, PhotocoreModSystem owner, IAttribute data, bool isConfigLibPath)
        {
            if (isConfigLibPath && sapi.Server.IsDedicated) return;

            if (isConfigLibPath)
            {
                string playerUid = (data as ITreeAttribute)?.GetAsString("player") ?? string.Empty;
                if (sapi.World.PlayerByUid(playerUid) is not IServerPlayer player) return;
                if (!player.HasPrivilege(Privilege.controlserver)) return;
            }

            // Pushed from the saving mod's own network handler, so this already runs on the server main thread.
            owner.ApplyConfig(ConfigLifecycle.LoadOrCreate(sapi, PhotocoreModSystem.ConfigFileName));

            // Already-connected players were sent the old authoritative values when they joined and are
            // never told again on their own, so re-push now that the server's config has changed. Without
            // this, a host tweaking ApplyFinishingEffects mid-session would keep producing photos that
            // differ from every guest's until they reconnect.
            owner.CameraCaptureBridge.BroadcastServerConfigOverride(sapi);
        }
    }
}
