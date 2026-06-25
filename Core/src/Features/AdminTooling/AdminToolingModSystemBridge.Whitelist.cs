using Photochemistry.AdminTooling.Whitelist;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Photochemistry.AdminTooling
{
    // Develop-whitelist ownership and the thin client-awareness sync. The server owns the authoritative
    // ExposureWhitelistService and pushes each player a DevelopPermissionPacket on join / on change; the
    // client caches the latest verdict so it can refuse to seal an exposure (and keep it) before the
    // server would reject the upload. Server enforcement is authoritative — this is purely UX safety.
    internal sealed partial class AdminToolingModSystemBridge
    {
        private ExposureWhitelistService? _exposureWhitelist;
        private PlayerDelegate? _playerJoinHandler;

        // Null until server operator-tooling startup has run. Read by the capture-finalization gate.
        internal ExposureWhitelistService? ExposureWhitelist => _exposureWhitelist;

        // Latest develop permission for the local client. Defaults to true so single-player and
        // whitelist-disabled servers behave exactly as before until (and unless) told otherwise.
        internal bool ClientDevelopAllowed { get; private set; } = true;

        internal void ConfigureServerWhitelistStartup(ICoreServerAPI api)
        {
            _exposureWhitelist = ExposureWhitelistService.LoadOrCreate(api, PhotochemistryModSystem.ServerDevelopWhitelistFileName);

            _playerJoinHandler = player => PushDevelopPermission(player);
            api.Event.PlayerJoin += _playerJoinHandler;
        }

        internal void DisposeServerWhitelist(ICoreServerAPI api)
        {
            if (_playerJoinHandler != null)
            {
                BestEffort.Try(_owner.BestEffortLogger, "unsubscribe develop-whitelist player join",
                    () => api.Event.PlayerJoin -= _playerJoinHandler);
                _playerJoinHandler = null;
            }
            _exposureWhitelist = null;
        }

        // Sends one player their current develop permission. Safe to call before the channel exists
        // (startup ordering): it no-ops until ServerChannel is wired, and PlayerJoin fires post-startup.
        internal void PushDevelopPermission(IServerPlayer player)
        {
            if (player == null || _owner.ServerChannel == null || _exposureWhitelist == null) return;
            _owner.ServerChannel.SendPacket(new DevelopPermissionPacket { Allowed = _exposureWhitelist.IsAllowed(player) }, player);
        }

        internal void BroadcastDevelopPermission(ICoreServerAPI api)
        {
            if (_owner.ServerChannel == null || _exposureWhitelist == null) return;
            foreach (IServerPlayer player in api.World.AllOnlinePlayers.OfType<IServerPlayer>())
                PushDevelopPermission(player);
        }

        internal void ConfigureClientDevelopPermissionChannelHandler()
        {
            if (_owner.ClientChannel == null) return;
            _owner.ClientChannel.SetMessageHandler<DevelopPermissionPacket>(OnDevelopPermissionReceived);
        }

        private void OnDevelopPermissionReceived(DevelopPermissionPacket packet)
        {
            if (packet == null) return;
            ClientDevelopAllowed = packet.Allowed;
        }
    }
}
