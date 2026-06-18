using Photochemistry.AdminTooling;
using Photochemistry.CameraCapture;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Photochemistry.FieldCamera
{
    // Owner handle + ambient API/config/logger accessors shared by all partials.
    // Capture is the seam handle for reaching CameraCapture pipeline members.
    internal sealed partial class FieldCameraModSystemBridge
    {
        private readonly PhotochemistryModSystem _owner;

        internal FieldCameraModSystemBridge(PhotochemistryModSystem owner)
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
