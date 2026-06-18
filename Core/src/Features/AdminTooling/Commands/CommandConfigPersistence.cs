namespace Photochemistry.AdminTooling
{
    // Feature-owned command persistence helpers for operator-tooling config writes.
    internal static class CommandConfigPersistence
    {
        // Persists preview command edits when a branch actually changed state.
        internal static void PersistPreviewConfig(PhotochemistryModSystem mod, PhotochemistryConfig rootCfg, bool changed)
        {
            if (mod.ClientApi == null || !changed) return;

            rootCfg.Viewfinder ??= new ViewfinderConfig();
            rootCfg.Viewfinder.ClampInPlace();
            mod.SaveClientConfig(mod.ClientApi);
        }
    }
}
