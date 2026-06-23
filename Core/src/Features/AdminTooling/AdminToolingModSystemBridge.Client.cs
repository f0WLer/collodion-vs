using System.Linq;
using Photochemistry.CameraCapture;
using Vintagestory.API.Client;

namespace Photochemistry.AdminTooling
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
            PhotochemistryModSystem.ClientInstance = _owner;
            _owner.ClientChannel = api.Network.GetChannel("photochemistry");
        }

        private void ConfigureClientOperatorToolingConfig(ICoreClientAPI api)
        {
            _owner.ApplyConfig(ConfigLifecycle.LoadOrCreate(api, PhotochemistryModSystem.ConfigFileName));
        }

        private void TryReportClientOperatorToolingStartupInfo()
        {
            var asm = typeof(PhotochemistryModSystem).Assembly;
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
                    () => _owner.ClientApi?.ShowChatMessage($"photochemistry: loaded mod dll (ver={ver}, build={stamp})"));
            }
        }

        private GuiDialogExposurePhysics? _exposurePhysicsDialog;

        internal void ConfigureClientOperatorToolingCommands(ICoreClientAPI api)
        {
            #pragma warning disable CS0618 // Keep legacy command registration for compatibility
            api.RegisterCommand(
                "photochemistry",
                "Collodion mod commands",
                ".collodion clearcache | .collodion export | .collodion preview | .collodion effects",
                OnModClientCommand
            );
            #pragma warning restore CS0618

            api.Input.RegisterHotKey(
                "photochemistry-exposuregui",
                "photochemistry: Open Exposure Physics GUI",
                GlKeys.Unknown,
                HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("photochemistry-exposuregui", _ =>
            {
                // Operator-only debug tooling. Gate on the server-operator privilege ("/op" grants it;
                // single-player and LAN hosts have it by default).
                if (api.World.Player?.Privileges?.Contains("controlserver") != true)
                {
                    api.ShowChatMessage("photochemistry: the exposure physics tuner requires operator privileges.");
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
