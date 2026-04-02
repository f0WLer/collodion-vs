using System;
using System.Globalization;
using Vintagestory.API.Common;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private void HandleWetplateVersionCommand()
        {
            if (ClientApi == null) return;

            try
            {
                var asm = typeof(CollodionModSystem).Assembly;
                string ver = asm.GetName().Version?.ToString() ?? "<nover>";
                string loc = asm.Location;
                string stamp = "<unknown>";
                try
                {
                    if (!string.IsNullOrEmpty(loc) && System.IO.File.Exists(loc))
                    {
                        stamp = System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
                catch { /* intentional: best-effort non-critical path */ }

                ClientApi.ShowChatMessage($"Wetplate: dll ver={ver} build={stamp}");
                ClientApi.ShowChatMessage($"Wetplate: dll path={loc}");
            }
            catch
            {
                ClientApi.ShowChatMessage("Wetplate: version info unavailable.");
            }
        }

        private void HandleWetplateClearCacheCommand()
        {
            if (ClientApi == null) return;

            int clearedPhotos = ItemFramedPhotograph.ClearClientRenderCacheAndBumpVersion();
            int clearedPlates = PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();
            ClientApi.ShowChatMessage($"Wetplate: cleared {clearedPhotos} photo renders + {clearedPlates} plate renders (new photos will re-load from disk). ");
        }

        private void HandleWetplateHudCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            string action = args.PopWord()?.ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(action))
            {
                ClientApi.ShowChatMessage("Wetplate: usage: .collodion hud <hide|show>");
                return;
            }

            bool hide = action == "hide" || action == "off" || action == "disable";
            bool show = action == "show" || action == "on" || action == "enable";
            if (!hide && !show)
            {
                ClientApi.ShowChatMessage("Wetplate: usage: .collodion hud <hide|show>");
                return;
            }

            ApplyHudHidden(hide);
            ClientApi.ShowChatMessage($"Wetplate: HUD {(hide ? "hidden" : "shown")} via {(hudHideMechanism ?? "<unknown>")}");
        }

        private void HandleWetplatePreviewCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            var cfg = GetOrLoadClientConfig(ClientApi);
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
                            ClientApi.ShowChatMessage("Wetplate: usage: .collodion preview peak [show|on|off|toggle]");
                            return;
                    }
                    break;

                case "on":
                case "enable":
                    cfg.Viewfinder.DebugPreviewEnabled = true;
                    changed = true;
                    break;

                case "off":
                case "disable":
                    cfg.Viewfinder.DebugPreviewEnabled = false;
                    changed = true;
                    break;

                case "toggle":
                    cfg.Viewfinder.DebugPreviewEnabled = !cfg.Viewfinder.DebugPreviewEnabled;
                    changed = true;
                    break;

                case "size":
                {
                    string wStr = args.PopWord();
                    string hStr = args.PopWord();
                    if (!int.TryParse(wStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                        || !int.TryParse(hStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                    {
                        ClientApi.ShowChatMessage("Wetplate: usage: .collodion preview size <width> <height>");
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
                        ClientApi.ShowChatMessage("Wetplate: usage: .collodion preview refresh <milliseconds>");
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
                        ClientApi.ShowChatMessage("Wetplate: usage: .collodion preview anchor <topleft|topright|bottomleft|bottomright>");
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
                        ClientApi.ShowChatMessage($"Wetplate: usage: .collodion preview quality <pixels> (current: {cfg.Viewfinder.DebugPreviewMaxDimension}, plate capture: {cfg.Viewfinder.PhotoCaptureMaxDimension})");
                        return;
                    }

                    cfg.Viewfinder.DebugPreviewMaxDimension = dim;
                    changed = true;
                    break;
                }

                default:
                    ClientApi.ShowChatMessage("Wetplate: usage: .collodion preview <show|on|off|toggle|size <w> <h>|refresh <ms>|anchor <pos>|peak [show|on|off|toggle]|quality <pixels>>");
                    return;
            }

            cfg.Viewfinder.ClampInPlace();

            if (changed)
            {
                SaveClientConfig(ClientApi);
            }

            ClientApi.ShowChatMessage(
                $"Wetplate: preview {(cfg.Viewfinder.DebugPreviewEnabled ? "on" : "off")}, "
                + $"{cfg.Viewfinder.DebugPreviewWidth}x{cfg.Viewfinder.DebugPreviewHeight}, "
                + $"refresh={cfg.Viewfinder.DebugPreviewRefreshMs}ms, anchor={cfg.Viewfinder.DebugPreviewAnchor}, "
                + $"peak={(cfg.Viewfinder.DebugPreviewPeak ? "on" : "off")}, "
                + $"quality={cfg.Viewfinder.DebugPreviewMaxDimension}px (plate={cfg.Viewfinder.PhotoCaptureMaxDimension}px)");
        }

        private void HandleSetProcessCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            string processId = args.PopWord()?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(processId))
            {
                string known = string.Join("|", Processes.AllProcesses.Keys);
                ClientApi.ShowChatMessage($"Usage: .collodion setprocess <{known}>");
                return;
            }

            var process = Processes.ResolveOrDefault(processId);
            if (!process.Id.Equals(processId, StringComparison.OrdinalIgnoreCase))
            {
                string known = string.Join("|", Processes.AllProcesses.Keys);
                ClientApi.ShowChatMessage($"Collodion: unknown process '{processId}'. Known: {known}");
                return;
            }

            // Determine which slot holds the plate: prefer active hotbar, fall back to offhand.
            ItemStack? hotbarStack = ClientApi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            ItemStack? offhandStack = ClientApi.World.Player?.InventoryManager?.OffhandHotbarSlot?.Itemstack;

            bool useOffhand = hotbarStack == null && offhandStack != null;

            if (hotbarStack == null && offhandStack == null)
            {
                ClientApi.ShowChatMessage("Collodion: hold a plate in your active hotbar or offhand slot.");
                return;
            }

            // Send server-authoritative packet — the server will validate and stamp the attribute.
            ClientChannel?.SendPacket(new SetPlateProcessPacket
            {
                ProcessId = process.Id,
                UseOffhand = useOffhand
            });
        }
    }
}

