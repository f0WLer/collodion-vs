using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion.AdminTooling
{
    // Lightweight helpers to resolve CollodionModSystem and config snapshots from APIs.
    // Avoids repeating ModLoader lookup boilerplate across files.
    internal static class CollodionConfigAccess
    {
        // Resolves the shared mod system instance from a generic core API handle.
        internal static CollodionModSystem? ResolveModSystem(ICoreAPI? api)
        {
            return api?.ModLoader?.GetModSystem<CollodionModSystem>();
        }

        // Resolves the client mod system, preferring the cached singleton when available.
        internal static CollodionModSystem? ResolveClientModSystem(ICoreClientAPI? capi)
        {
            return CollodionModSystem.ClientInstance ?? capi?.ModLoader?.GetModSystem<CollodionModSystem>();
        }

        // Returns the shared runtime config snapshot for the provided API context.
        internal static CollodionConfig? ResolveConfig(ICoreAPI? api)
        {
            return ResolveModSystem(api)?.Config;
        }

        // Returns the client-side config snapshot for rendering/input code paths.
        internal static CollodionConfig? ResolveClientConfig(ICoreClientAPI? capi)
        {
            return ResolveClientModSystem(capi)?.Config;
        }
    }
}
