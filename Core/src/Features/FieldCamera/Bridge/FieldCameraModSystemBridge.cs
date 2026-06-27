using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

using Photocore.Configuration;
using Photocore.CameraCapture;

namespace Photocore.FieldCamera
{
    // Owner handle + ambient API/config/logger accessors shared by all partials.
    // Capture is the seam handle for reaching CameraCapture pipeline members.
    internal sealed partial class FieldCameraModSystemBridge
    {
        private readonly PhotocoreModSystem _owner;

        internal FieldCameraModSystemBridge(PhotocoreModSystem owner)
        {
            _owner = owner;
        }

        internal CameraCaptureModSystemBridge Capture => _owner.CameraCaptureBridge;

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
