using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    internal static class CollodionConfigAccess
    {
        internal static CollodionModSystem? ResolveModSystem(ICoreAPI? api)
        {
            return api?.ModLoader?.GetModSystem<CollodionModSystem>();
        }

        internal static CollodionModSystem? ResolveClientModSystem(ICoreClientAPI? capi)
        {
            return CollodionModSystem.ClientInstance ?? capi?.ModLoader?.GetModSystem<CollodionModSystem>();
        }

        internal static CollodionConfig? ResolveConfig(ICoreAPI? api)
        {
            return ResolveModSystem(api)?.Config;
        }

        internal static CollodionConfig? ResolveClientConfig(ICoreClientAPI? capi)
        {
            return ResolveClientModSystem(capi)?.Config;
        }
    }
}
