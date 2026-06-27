using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Photocore.Configuration
{
    // Lightweight helpers to resolve PhotocoreModSystem and config snapshots from APIs.
    // Avoids repeating ModLoader lookup boilerplate across files.
    internal static class PhotocoreConfigAccess
    {
        // Resolves the shared mod system instance from a generic core API handle.
        internal static PhotocoreModSystem? ResolveModSystem(ICoreAPI? api)
        {
            return api?.ModLoader?.GetModSystem<PhotocoreModSystem>();
        }

        // Resolves the client mod system, preferring the cached singleton when available.
        internal static PhotocoreModSystem? ResolveClientModSystem(ICoreClientAPI? capi)
        {
            return PhotocoreModSystem.ClientInstance ?? capi?.ModLoader?.GetModSystem<PhotocoreModSystem>();
        }

        // Returns the shared runtime config snapshot for the provided API context.
        internal static PhotocoreConfig? ResolveConfig(ICoreAPI? api)
        {
            return ResolveModSystem(api)?.Config;
        }

        // Returns the client-side config snapshot for rendering/input code paths.
        internal static PhotocoreConfig? ResolveClientConfig(ICoreClientAPI? capi)
        {
            return ResolveClientModSystem(capi)?.Config;
        }
    }
}
