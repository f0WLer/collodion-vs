using System;

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
                catch { }

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

            int clearedPhotos = ItemPhotograph.ClearClientRenderCacheAndBumpVersion();
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
    }
}
