using Photocore.CameraCapture;
using Vintagestory.API.Client;
using Photocore.Configuration;

namespace Photocore.AdminTooling
{
    // Client-side operator-tooling startup composition.
    // Keeps config bootstrap and startup diagnostics out of ModSystem callback bodies.
    internal sealed partial class AdminToolingModSystemBridge
    {
        // Composes full client-side operator-tooling startup so ModSystem root stays declarative.
        internal void ConfigureClientOperatorToolingStartup(ICoreClientAPI api)
        {
            ConfigureClientOperatorToolingCore(api);
            ConfigureClientOperatorToolingConfig(api);
            TryReportClientOperatorToolingStartupInfo();
            ConfigureClientOperatorToolingCommands(api);
        }

        private void ConfigureClientOperatorToolingCore(ICoreClientAPI api)
        {
            _owner.ClientApi = api;
            PhotocoreModSystem.ClientInstance = _owner;
            _owner.ClientChannel = api.Network.GetChannel("photocore");
        }

        private void ConfigureClientOperatorToolingConfig(ICoreClientAPI api)
        {
            _owner.ApplyConfig(ConfigLifecycle.LoadOrCreate(api, PhotocoreModSystem.ConfigFileName));
        }

        private void TryReportClientOperatorToolingStartupInfo()
        {
            var asm = typeof(PhotocoreModSystem).Assembly;
            string ver = asm.GetName().Version?.ToString() ?? "<nover>";
            string loc = asm.Location;
            string stamp = string.IsNullOrEmpty(loc)
                ? "<unknown>"
                : BestEffort.Try(_owner.BestEffortLogger,
                    "read client dll timestamp",
                    () => File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss"),
                    "<unknown>");

            if (_owner.ClientConfig?.ShowDebugLogs == true)
            {
                BestEffort.Try(_owner.BestEffortLogger,
                    "report client startup version info",
                    () => _owner.ClientApi?.ShowChatMessage($"photocore: loaded mod dll (ver={ver}, build={stamp})"));
            }
        }

        private GuiDialogExposurePhysics? _exposurePhysicsDialog;

        internal void ConfigureClientOperatorToolingCommands(ICoreClientAPI api)
        {
            #pragma warning disable CS0618 // Keep legacy command registration for compatibility
            api.RegisterCommand(
                "photocore",
                "photocore mod commands",
                SubcommandList,
                OnModClientCommand
            );
            #pragma warning restore CS0618

            api.Input.RegisterHotKey(
                "photocore-exposuregui",
                "photocore: Open Exposure Physics GUI",
                GlKeys.Unknown,
                HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("photocore-exposuregui", _ =>
            {
                // Operator-only debug tooling. Gate on the server-operator privilege ("/op" grants it;
                // single-player and LAN hosts have it by default).
                if (api.World.Player?.Privileges?.Contains("controlserver") != true)
                {
                    api.ShowChatMessage("photocore: the exposure physics tuner requires operator privileges.");
                    return false;
                }

                VirtualExposureRenderer? renderer = _owner.CameraCaptureBridge._virtualExposureRenderer;
                if (renderer == null) return false;
                _exposurePhysicsDialog ??= new GuiDialogExposurePhysics(api, renderer, _owner);
                if (_exposurePhysicsDialog.IsOpened()) _exposurePhysicsDialog.TryClose();
                else _exposurePhysicsDialog.TryOpen();
                return true;
            });
        }
    }
}
