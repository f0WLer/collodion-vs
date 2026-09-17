using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

using Photocore.Configuration;

namespace Photocore
{
    // Owner handle + ambient API/config/logger accessors shared by every feature bridge.
    internal abstract class ModSystemBridgeBase
    {
        protected readonly PhotocoreModSystem _owner;

        protected ModSystemBridgeBase(PhotocoreModSystem owner)
        {
            _owner = owner;
        }

        internal ICoreAPI? Api => _owner.ModApi;
        internal ICoreClientAPI? ClientApi => _owner.ClientApi;
        internal IClientNetworkChannel? ClientChannel => _owner.ClientChannel;
        internal IServerNetworkChannel? ServerChannel
        {
            get => _owner.ServerChannel;
            set => _owner.ServerChannel = value;
        }

        internal PhotocoreConfig Config
        {
            get => _owner.Config;
            set
            {
                _owner.Config = value;
                _owner.ClientConfig = value.Client;
            }
        }

        internal PhotocoreClientConfig ClientConfig => _owner.ClientConfig;
        internal ILogger? BestEffortLogger => _owner.BestEffortLogger;
        internal bool IsBestEffortDebugLoggingEnabled => _owner.IsBestEffortDebugLoggingEnabled;
    }
}
