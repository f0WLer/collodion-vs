namespace Photocore.PhotoMetadata
{
    // Detects whether the current world's scoped photo store folder is also used by another,
    // divergent copy of the same save (the only realistic SavegameIdentifier collision -- see
    // DESIGN-photo-store-scoping.md). CurrentWorldFilepath differs between copies even though the
    // savegameId is identical, so it is the discriminator the marker compares.
    internal static class PhotoStoreWorldMarker
    {
        private const string MarkerFileName = "world.txt";

        // Reads any existing marker, compares it to the current world, then overwrites it with the
        // current world's identity. Returns true when an existing marker names a DIFFERENT save path
        // -- i.e. this store folder is shared by more than one world copy. Best-effort: any I/O
        // failure is treated as "not shared" (a missed warning is safer than blocking startup).
        internal static bool CheckAndUpdate(string storeDirectory, string currentWorldFilepath, string? currentWorldName)
        {
            bool shared = false;
            try
            {
                Directory.CreateDirectory(storeDirectory);
                string markerPath = Path.Combine(storeDirectory, MarkerFileName);

                if (File.Exists(markerPath))
                {
                    string[] lines = File.ReadAllLines(markerPath);
                    string? previousPath = lines.Length > 0 ? lines[0] : null;
                    if (!string.IsNullOrEmpty(previousPath)
                        && !string.Equals(previousPath, currentWorldFilepath, StringComparison.Ordinal))
                    {
                        shared = true;
                    }
                }

                File.WriteAllLines(markerPath, new[] { currentWorldFilepath, currentWorldName ?? string.Empty });
            }
            catch
            {
                // Best-effort tripwire only.
            }
            return shared;
        }
    }
}
