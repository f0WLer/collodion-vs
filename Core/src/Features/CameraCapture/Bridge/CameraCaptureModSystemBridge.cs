using Photochemistry.Configuration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Photochemistry.CameraCapture
{
    // Owner handle + ambient API/config/logger accessors shared by all partials.
    internal sealed partial class CameraCaptureModSystemBridge
    {
    // CameraCapture startup composition and packet registration wiring.
    // Keeps camera packet registration ownership in the CameraCapture feature root.

        private readonly PhotochemistryModSystem _owner;

        internal CameraCaptureModSystemBridge(PhotochemistryModSystem owner)
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

        internal PhotochemistryConfig Config
        {
            get => _owner.Config;
            set
            {
                _owner.Config = value;
                _owner.ClientConfig = value.Client;
            }
        }

        internal PhotochemistryClientConfig ClientConfig => _owner.ClientConfig;
        internal ILogger? BestEffortLogger => _owner.BestEffortLogger;
        internal bool IsBestEffortDebugLoggingEnabled => _owner.IsBestEffortDebugLoggingEnabled;
    }
}
