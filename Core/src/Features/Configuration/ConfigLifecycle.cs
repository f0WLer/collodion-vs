using Vintagestory.API.Common;

namespace Photocore.Configuration
{
    // Feature-owned config lifecycle policy used by client/server startup and runtime persistence paths.
    // Normalizes config trees consistently and keeps load/store error handling in one place.
    internal static class ConfigLifecycle
    {
        // Loads persisted config, falling back to defaults when missing/invalid. Always rewrites the
        // file after normalizing so its on-disk shape stays complete (e.g. a newly added field appears
        // for existing players too) — needed for external tools like ConfigLib that bind directly to
        // this file's paths rather than going through EnsureNormalized/ClampInPlace themselves.
        internal static PhotocoreConfig LoadOrCreate(ICoreAPICommon api, string fileName)
        {
            PhotocoreConfig? cfg;
            try
            {
                cfg = api.LoadModConfig<PhotocoreConfig>(fileName);
            }
            catch
            {
                cfg = null;
            }

            cfg = EnsureNormalized(cfg);
            TryStore(api, fileName, cfg);

            return cfg;
        }

        // Ensures a non-null, clamped config tree suitable for runtime reads.
        internal static PhotocoreConfig EnsureNormalized(PhotocoreConfig? cfg)
        {
            cfg ??= new PhotocoreConfig();
            cfg.ClampInPlace();
            return cfg;
        }

        // Stores config best-effort without throwing into gameplay paths.
        internal static void TryStore(ICoreAPICommon api, string fileName, PhotocoreConfig cfg)
        {
            try
            {
                api.StoreModConfig(cfg, fileName);
            }
            catch
            {
                // ignore
            }
        }

        // Note: there is deliberately no "store the live in-memory config" helper here. On a client
        // connected to a server, the in-memory config carries that server's authoritative overrides, so
        // persisting it would write another server's settings into the player's own photocore.json.
        // LoadOrCreate above only ever writes back what it just read from disk.
    }
}
