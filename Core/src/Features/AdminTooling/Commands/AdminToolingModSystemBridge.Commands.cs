using System.Globalization;
using Photochemistry.ImageEffects;
using Photochemistry.Plates.Rendering;
using Photochemistry.Configuration;

namespace Photochemistry.AdminTooling
{
    // Client command router for .photochemistry subcommands.
    // Dispatches to command partials without mixing command parsing into gameplay flows.
    internal sealed partial class AdminToolingModSystemBridge
    {
        private const string PhotoplatePreviewCommandArgs = "show|on|off|toggle|size <w> <h>|refresh <ms>|anchor <pos>|peak [show|on|off|toggle]|quality <px>";
        private const string SubcommandList = "clearcache | clearpex [confirm] | export | preview (" + PhotoplatePreviewCommandArgs + ") | effects | effect <FieldName> <value> | effect save | effect load";
        private const string AvailableCommandsLine = "photochemistry: available commands: " + SubcommandList;
        private const string UnknownCommandTryLine = "Try: .photochemistry " + SubcommandList;

        // Routes .photochemistry subcommands to their specialized handler partials.
        internal void OnModClientCommand(int groupId, Vintagestory.API.Common.CmdArgs args)
        {
            if (_owner.ClientApi == null) return;

            string sub = args.PopWord();
            if (string.IsNullOrEmpty(sub))
            {
                _owner.ClientApi.ShowChatMessage(AvailableCommandsLine);
                return;
            }

            if (sub.Equals("clearcache", StringComparison.OrdinalIgnoreCase))
            {
                HandleModClearCacheCommand();
                return;
            }

            if (sub.Equals("clearpex", StringComparison.OrdinalIgnoreCase))
            {
                HandleClearPexCommand(args);
                return;
            }

            if (sub.Equals("export", StringComparison.OrdinalIgnoreCase))
            {
                HandleModExportCommand();
                return;
            }

            if (sub.Equals("effects", StringComparison.OrdinalIgnoreCase) || sub.Equals("fx", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsCommandHandler.HandleEffectsCommand(_owner.ClientApi, args);
                return;
            }

            if (sub.Equals("preview", StringComparison.OrdinalIgnoreCase))
            {
                HandlePhotoplatePreviewCommand(args);
                return;
            }

            _owner.ClientApi.ShowChatMessage($"photochemistry: unknown subcommand '{sub}'. {UnknownCommandTryLine}");
        }

        // Clears local photo and plate render caches so existing media is reloaded from disk.
        internal void HandleModClearCacheCommand()
        {
            if (_owner.ClientApi == null) return;

            int clearedPlates = PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();
            _owner.ClientApi.ShowChatMessage($"photochemistry: cleared {clearedPlates} plate renders (new photos will re-load from disk).");
        }

        // Handles debug preview toggles and sizing/quality subcommands for live viewfinder diagnostics.
        internal void HandlePhotoplatePreviewCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (_owner.ClientApi == null) return;

            var cfg = _owner.GetOrLoadClientConfig(_owner.ClientApi);
            cfg.Viewfinder ??= new ViewfinderConfig();

            string action = args.PopWord()?.ToLowerInvariant() ?? "show";
            bool changed = false;

            switch (action)
            {
                case "show":
                    break;

                case "peak":
                    string peakAction = args.PopWord()?.ToLowerInvariant() ?? "on";
                    switch (peakAction)
                    {
                        case "show":
                            break;

                        case "on":
                        case "enable":
                            cfg.Viewfinder.DebugPreviewPeak = true;
                            changed = true;
                            break;

                        case "off":
                        case "disable":
                            cfg.Viewfinder.DebugPreviewPeak = false;
                            changed = true;
                            break;

                        case "toggle":
                            cfg.Viewfinder.DebugPreviewPeak = !cfg.Viewfinder.DebugPreviewPeak;
                            changed = true;
                            break;

                        default:
                            _owner.ClientApi.ShowChatMessage("usage: .photochemistry preview peak [show|on|off|toggle]");
                            return;
                    }
                    break;

                case "on":
                case "enable":
                    cfg.Viewfinder.DebugPreviewPeak = true;
                    changed = true;
                    break;

                case "off":
                case "disable":
                    cfg.Viewfinder.DebugPreviewPeak = false;
                    changed = true;
                    break;

                case "toggle":
                    cfg.Viewfinder.DebugPreviewPeak = !cfg.Viewfinder.DebugPreviewPeak;
                    changed = true;
                    break;

                case "size":
                    {
                        string wStr = args.PopWord();
                        string hStr = args.PopWord();
                        if (!int.TryParse(wStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                            || !int.TryParse(hStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                        {
                            _owner.ClientApi.ShowChatMessage("usage: .photochemistry preview size <width> <height>");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewWidth = w;
                        cfg.Viewfinder.DebugPreviewHeight = h;
                        changed = true;
                        break;
                    }

                case "refresh":
                    {
                        string msStr = args.PopWord();
                        if (!int.TryParse(msStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms))
                        {
                            _owner.ClientApi.ShowChatMessage("usage: .photochemistry preview refresh <milliseconds>");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewRefreshMs = ms;
                        changed = true;
                        break;
                    }

                case "anchor":
                    {
                        string anchor = args.PopWord();
                        if (string.IsNullOrWhiteSpace(anchor))
                        {
                            _owner.ClientApi.ShowChatMessage("usage: .photochemistry preview anchor <topleft|topright|bottomleft|bottomright>");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewAnchor = anchor;
                        changed = true;
                        break;
                    }

                case "quality":
                    {
                        string dimStr = args.PopWord();
                        if (!int.TryParse(dimStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dim))
                        {
                            _owner.ClientApi.ShowChatMessage($"usage: .photochemistry preview quality <pixels> (current: {cfg.Viewfinder.DebugPreviewMaxDimension}, plate capture: {cfg.Viewfinder.PhotoCaptureMaxDimension})");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewMaxDimension = dim;
                        changed = true;
                        break;
                    }

                default:
                    _owner.ClientApi.ShowChatMessage("usage: .photochemistry preview <show|on|off|toggle|size <w> <h>|refresh <ms>|anchor <pos>|peak [show|on|off|toggle]|quality <pixels>>");
                    return;
            }

            if (changed)
            {
                cfg.Viewfinder ??= new ViewfinderConfig();
                cfg.Viewfinder.ClampInPlace();
                _owner.SaveClientConfig(_owner.ClientApi);
            }

            _owner.ClientApi.ShowChatMessage(
                $"photochemistry: preview {cfg.Viewfinder.DebugPreviewWidth}x{cfg.Viewfinder.DebugPreviewHeight}, "
                + $"refresh={cfg.Viewfinder.DebugPreviewRefreshMs}ms, anchor={cfg.Viewfinder.DebugPreviewAnchor}, "
                + $"peak={(cfg.Viewfinder.DebugPreviewPeak ? "on" : "off")}, "
                + $"quality={cfg.Viewfinder.DebugPreviewMaxDimension}px (plate={cfg.Viewfinder.PhotoCaptureMaxDimension}px)");
        }
    }
}
