using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Photochemistry.Configuration
{
    // Lightweight helpers to resolve PhotochemistryModSystem and config snapshots from APIs.
    // Avoids repeating ModLoader lookup boilerplate across files.
    internal static class PhotochemistryConfigAccess
    {
        // Resolves the shared mod system instance from a generic core API handle.
        internal static PhotochemistryModSystem? ResolveModSystem(ICoreAPI? api)
        {
            return api?.ModLoader?.GetModSystem<PhotochemistryModSystem>();
        }

        // Resolves the client mod system, preferring the cached singleton when available.
        internal static PhotochemistryModSystem? ResolveClientModSystem(ICoreClientAPI? capi)
        {
            return PhotochemistryModSystem.ClientInstance ?? capi?.ModLoader?.GetModSystem<PhotochemistryModSystem>();
        }

        // Returns the shared runtime config snapshot for the provided API context.
        internal static PhotochemistryConfig? ResolveConfig(ICoreAPI? api)
        {
            return ResolveModSystem(api)?.Config;
        }

        // Returns the client-side config snapshot for rendering/input code paths.
        internal static PhotochemistryConfig? ResolveClientConfig(ICoreClientAPI? capi)
        {
            return ResolveClientModSystem(capi)?.Config;
        }
    }
}
