using Vintagestory.API.Common;
using Photocore.Configuration;

namespace Photocore
{
    // Server-only, config-gated diagnostic logging. Silent unless the host enables
    // Client.ShowDebugLogs in their server collodion.json. Emits at Notification level so
    // the lines land in server-main.log (easy to send) rather than the noisier debug log.
    // Generic on purpose: callers supply their own message prefix so this is reusable for
    // any server-side diagnostic, not just plate interactions.
    internal static class ServerDebugLog
    {
        internal static void Notify(ICoreAPI? api, string format, params object[] args)
        {
            if (api == null || api.Side != EnumAppSide.Server) return;
            if (PhotocoreConfigAccess.ResolveConfig(api)?.Client?.ShowDebugLogs != true) return;
            Log.Notify(api.Logger, format, args);
        }
    }
}
