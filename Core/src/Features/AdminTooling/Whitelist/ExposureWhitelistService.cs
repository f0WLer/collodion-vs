using Vintagestory.API.Server;

namespace Photochemistry.AdminTooling.Whitelist
{
    // Server-side owner of the develop-whitelist: in-memory state + immediate persistence. Admin
    // mutations are rare, so each change writes the small JSON file synchronously (no dirty/flush tick,
    // unlike the high-churn last-seen index). The actual allow decision is the pure ExposureWhitelistLogic.
    internal sealed class ExposureWhitelistService
    {
        private readonly ICoreServerAPI _sapi;
        private readonly string _fileName;
        private readonly ExposureWhitelistState _state;

        private ExposureWhitelistService(ICoreServerAPI sapi, string fileName, ExposureWhitelistState state)
        {
            _sapi = sapi;
            _fileName = fileName;
            _state = state;
        }

        internal static ExposureWhitelistService LoadOrCreate(ICoreServerAPI sapi, string fileName)
        {
            ExposureWhitelistState? loaded = null;
            try
            {
                loaded = sapi.LoadModConfig<ExposureWhitelistState>(fileName);
            }
            catch
            {
                loaded = null;
            }

            if (loaded == null)
            {
                loaded = new ExposureWhitelistState();
                try { sapi.StoreModConfig(loaded, fileName); }
                catch { /* best-effort: persisted on the next mutation */ }
            }

            loaded.Normalize();
            return new ExposureWhitelistService(sapi, fileName, loaded);
        }

        internal bool Enabled => _state.Enabled;

        internal int Count => _state.Players.Count;

        // Authoritative per-player develop permission. When the whitelist is off everyone develops;
        // when on, operators (always — so they can't lock themselves out) and explicit members do.
        internal bool IsAllowed(IServerPlayer player)
        {
            if (player == null || !_state.Enabled) return true;
            return player.HasPrivilege(Privilege.controlserver) || _state.Players.ContainsKey(player.PlayerUID);
        }

        internal bool Contains(string playerUid)
            => !string.IsNullOrEmpty(playerUid) && _state.Players.ContainsKey(playerUid);

        internal string? GetName(string playerUid)
            => _state.Players.TryGetValue(playerUid, out string? name) ? name : null;

        internal bool SetEnabled(bool enabled)
        {
            if (_state.Enabled == enabled) return false;
            _state.Enabled = enabled;
            Persist();
            return true;
        }

        internal bool Add(string playerUid, string playerName)
        {
            if (string.IsNullOrEmpty(playerUid)) return false;
            bool isNew = !_state.Players.ContainsKey(playerUid);
            _state.Players[playerUid] = playerName ?? playerUid;
            Persist();
            return isNew;
        }

        internal bool Remove(string playerUid)
        {
            if (string.IsNullOrEmpty(playerUid)) return false;
            if (!_state.Players.Remove(playerUid)) return false;
            Persist();
            return true;
        }

        internal IReadOnlyDictionary<string, string> Snapshot()
            => new Dictionary<string, string>(_state.Players, StringComparer.Ordinal);

        private void Persist()
        {
            try { _sapi.StoreModConfig(_state, _fileName); }
            catch (Exception ex)
            {
                _sapi.Logger.Warning($"photochemistry: failed to persist develop whitelist '{_fileName}': {ex.Message}");
            }
        }
    }
}
