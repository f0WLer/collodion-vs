using System;
using Vintagestory.API.Common;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private const string WetplateAvailableCommandsLine = "Collodion: available commands: clearcache | hud (hide|show) | pose | effects";
        private const string WetplateUnknownCommandTryLine = "Try: .collodion clearcache | .collodion hud (hide|show) | .collodion pose | .collodion effects";

        private void OnWetplateClientCommand(int groupId, Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            string sub = args.PopWord();
            if (string.IsNullOrEmpty(sub))
            {
                ClientApi.ShowChatMessage(WetplateAvailableCommandsLine);
                return;
            }

            if (sub.Equals("ver", StringComparison.OrdinalIgnoreCase) || sub.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplateVersionCommand();
                return;
            }

            if (sub.Equals("clearcache", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplateClearCacheCommand();
                return;
            }

            if (sub.Equals("effects", StringComparison.OrdinalIgnoreCase) || sub.Equals("fx", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplateEffectsCommand(args);
                return;
            }

            if (sub.Equals("hud", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplateHudCommand(args);
                return;
            }

            if (sub.Equals("pose", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplatePoseCommand(args);
                return;
            }

            ClientApi.ShowChatMessage($"Collodion: unknown subcommand '{sub}'. {WetplateUnknownCommandTryLine}");
        }
    }
}
