namespace Photocore.AdminTooling.Whitelist
{
    // Persisted develop-whitelist state: whether the gate is active, and the allowed players keyed by
    // UID (value = last-known name, for human-readable command output). Round-tripped via StoreModConfig
    // to photocore-develop-whitelist.json. Disabled with no members is the default (no behaviour change).
    internal sealed class ExposureWhitelistState
    {
        public bool Enabled { get; set; }

        // UID -> last-known player name. UID is the stable key; the name is display-only and refreshed on add.
        public Dictionary<string, string> Players { get; set; } = new(StringComparer.Ordinal);

        // Guards against a null dictionary after deserialising a hand-edited or partial file.
        public void Normalize()
        {
            Players ??= new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
