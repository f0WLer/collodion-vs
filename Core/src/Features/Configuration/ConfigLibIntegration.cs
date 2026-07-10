using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Photocore.Configuration
{
    // Live reload of photocore.json when the optional ConfigLib mod (github.com/maltiez2/vsmod_configlib)
    // saves it from its in-game settings GUI.
    //
    // This deliberately references no ConfigLib types. ConfigLib publishes its signals on the game's own
    // event bus under a documented, domain-scoped event name, so listening for that string needs no
    // assembly reference, no build flag, and no conditional compilation. Per ConfigLib's wiki: "Mods do
    // not need to depend on the library, unless using the C# api." Without ConfigLib installed the event
    // is simply never pushed and these listeners never fire, which is the whole soft-dependency story.
    //
    // ConfigLib patches photocore.json directly (see assets/photocore/config/configlib-patches.json), so
    // "reapply" is just re-running the same load path used at startup.
    internal static class ConfigLibIntegration
    {
        // ConfigLibModSystem.ConfigSavedEvent is "configlib:{0}:config-saved", formatted with the domain
        // owning the configlib-patches.json asset — ours. Fires once per actual save (not merely on the
        // settings window closing), after the file has been written.
        private const string ConfigSavedEvent = "configlib:photocore:config-saved";

        internal static void RegisterClient(ICoreClientAPI capi, PhotocoreModSystem owner)
        {
            capi.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) => OnClientConfigSaved(capi, owner),
                filterByEventName: ConfigSavedEvent);
        }

        internal static void RegisterServer(ICoreServerAPI sapi, PhotocoreModSystem owner)
        {
            sapi.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) => OnServerConfigSaved(sapi, owner, data),
                filterByEventName: ConfigSavedEvent);
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
        // ConfigLib relays this event to the server for every client's save, so both guards below matter:
        //
        // Dedicated: skipped, because the event arrives too early to be useful. ConfigLib sends the event
        // packet before the packet that actually rewrites the server's photocore.json, and both travel the
        // same channel in that order — so re-reading the file here would load the pre-save values. There is
        // no post-write event to hook instead (the server's write path only broadcasts a packet). Dedicated
        // servers therefore still pick up config changes on restart, as they always have.
        //
        // Integrated (singleplayer / LAN host): correct and fresh. Client and server share one
        // photocore.json, ConfigLib writes it before raising the event, and it skips its own server-side
        // write precisely because that file is already current.
        private static void OnServerConfigSaved(ICoreServerAPI sapi, PhotocoreModSystem owner, IAttribute data)
        {
            if (sapi.Server.IsDedicated) return;

            // ConfigLib does not gate the relayed event on privilege the way it gates its own server-side
            // setting writes, so an unprivileged player's save would otherwise trigger server disk reads.
            string playerUid = (data as ITreeAttribute)?.GetAsString("player") ?? string.Empty;
            if (sapi.World.PlayerByUid(playerUid) is not IServerPlayer player) return;
            if (!player.HasPrivilege(Privilege.controlserver)) return;

            // Pushed from ConfigLib's network handler, so this already runs on the server main thread.
            owner.ApplyConfig(ConfigLifecycle.LoadOrCreate(sapi, PhotocoreModSystem.ConfigFileName));

            // Already-connected players were sent the old authoritative values when they joined and are
            // never told again on their own, so re-push now that the server's config has changed. Without
            // this, a host tweaking ApplyFinishingEffects mid-session would keep producing photos that
            // differ from every guest's until they reconnect.
            owner.CameraCaptureBridge.BroadcastServerConfigOverride(sapi);
        }
    }
}
