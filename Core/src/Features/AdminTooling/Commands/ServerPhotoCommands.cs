using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

using Photocore.PhotoMetadata;
using Photocore.Configuration;

namespace Photocore.AdminTooling
{
    // Server-side operator commands for auditing and reclaiming the on-disk photo store, driven by the
    // last-seen index (see DESIGN-photo-disk-audit). Registered under /photoadmin, gated on controlserver.
    // Dry-run by default; the literal word 'confirm' executes a destructive action.
    internal sealed class ServerPhotoCommands
    {
        private const int DryRunIdSampleLimit = 15;

        private readonly ICoreServerAPI _sapi;
        private readonly PhotocoreModSystem _owner;

        private ServerPhotoCommands(ICoreServerAPI sapi, PhotocoreModSystem owner)
        {
            _sapi = sapi;
            _owner = owner;
        }

        internal static void Register(ICoreServerAPI sapi, PhotocoreModSystem owner)
            => new ServerPhotoCommands(sapi, owner).RegisterCommands();

        private void RegisterCommands()
        {
            var p = _sapi.ChatCommands.Parsers;

            _sapi.ChatCommands
                .Create("photoadmin")
                .WithDescription("Audit and reclaim the server's on-disk photo store via the last-seen index.")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("stats")
                    .WithDescription("Summarise the photo store: file count, bytes, and seen / never-seen / stale-index buckets.")
                    .HandleWith(OnStats)
                .EndSubCommand()
                .BeginSubCommand("audit")
                    .WithDescription("List the N least-recently-seen photos (default 20): id, last-seen age, added age, size.")
                    .WithArgs(p.OptionalInt("count", 20))
                    .HandleWith(OnAudit)
                .EndSubCommand()
                .BeginSubCommand("delete")
                    .WithDescription("Delete source photos (cascades derived masks + index rows). Dry-run unless 'confirm'.")
                    .BeginSubCommand("oldest")
                        .WithDescription("Delete the N least-recently-seen photos (grace-protected).")
                        .WithArgs(p.Int("count"), p.OptionalWord("confirm"))
                        .HandleWith(OnDeleteOldest)
                    .EndSubCommand()
                    .BeginSubCommand("olderthan")
                        .WithDescription("Delete every photo not seen in the last N days (grace-protected; includes never-seen).")
                        .WithArgs(p.Int("days"), p.OptionalWord("confirm"))
                        .HandleWith(OnDeleteOlderThan)
                    .EndSubCommand()
                    .BeginSubCommand("id")
                        .WithDescription("Delete specific photo ids (comma-separated, no spaces). Bypasses the grace period.")
                        .WithArgs(p.Word("ids"), p.OptionalWord("confirm"))
                        .HandleWith(OnDeleteById)
                    .EndSubCommand()
                .EndSubCommand()
                .BeginSubCommand("prune-index")
                    .WithDescription("Drop last-seen index rows whose backing photo file no longer exists. Dry-run unless 'confirm'.")
                    .WithArgs(p.OptionalWord("confirm"))
                    .HandleWith(OnPruneIndex)
                .EndSubCommand()
                .BeginSubCommand("whitelist")
                    .WithDescription("Restrict who may develop exposures (the act that stores photos on the server). Disabled by default.")
                    .BeginSubCommand("status")
                        .WithDescription("Show whether the develop whitelist is on and how many players it holds.")
                        .HandleWith(OnWhitelistStatus)
                    .EndSubCommand()
                    .BeginSubCommand("enable")
                        .WithDescription("Turn the develop whitelist on: only listed players (and operators) may develop.")
                        .HandleWith(OnWhitelistEnable)
                    .EndSubCommand()
                    .BeginSubCommand("disable")
                        .WithDescription("Turn the develop whitelist off: everyone may develop again.")
                        .HandleWith(OnWhitelistDisable)
                    .EndSubCommand()
                    .BeginSubCommand("add")
                        .WithDescription("Allow a player to develop. Accepts an online name or a known last-seen name.")
                        .WithArgs(p.Word("player"))
                        .HandleWith(OnWhitelistAdd)
                    .EndSubCommand()
                    .BeginSubCommand("remove")
                        .WithDescription("Revoke a player's develop permission.")
                        .WithArgs(p.Word("player"))
                        .HandleWith(OnWhitelistRemove)
                    .EndSubCommand()
                    .BeginSubCommand("list")
                        .WithDescription("List the players currently allowed to develop.")
                        .HandleWith(OnWhitelistList)
                    .EndSubCommand()
                .EndSubCommand();
        }

        private PhotoDiskAuditService? TryGetService(out string error)
        {
            ServerPhotoSeenService? seen = _owner.PhotoSyncModSystemBridge.PhotoSeenService;
            if (seen == null)
            {
                error = "the photo index is not initialised yet (server still starting up).";
                return null;
            }
            double grace = PhotocoreConfigAccess.ResolveConfig(_sapi)?.PhotoSync?.PhotoDeleteGraceHours ?? 24.0;
            error = string.Empty;
            return new PhotoDiskAuditService(seen, grace);
        }

        private TextCommandResult OnStats(TextCommandCallingArgs args)
        {
            PhotoDiskAuditService? svc = TryGetService(out string err);
            if (svc == null) return TextCommandResult.Error("photoadmin: " + err);

            AuditStats s = svc.GetStats();
            return TextCommandResult.Success(
                $"photoadmin: {s.TotalFiles} source photo(s), {FormatBytes(s.TotalBytes)} on disk.\n"
              + $"  seen: {s.SeenCount} ({FormatBytes(s.SeenBytes)})  ·  never-seen: {s.NeverSeenCount} ({FormatBytes(s.NeverSeenBytes)})  ·  stale index rows: {s.StaleIndexCount}");
        }

        private TextCommandResult OnAudit(TextCommandCallingArgs args)
        {
            PhotoDiskAuditService? svc = TryGetService(out string err);
            if (svc == null) return TextCommandResult.Error("photoadmin: " + err);

            int count = (int)args[0];
            if (count <= 0) count = 20;
            DateTime now = DateTime.UtcNow;

            IReadOnlyList<PhotoAuditRow> rows = svc.GetAudit(count);
            if (rows.Count == 0) return TextCommandResult.Success("photoadmin: no source photos on disk.");

            var sb = new StringBuilder();
            sb.Append($"photoadmin: {rows.Count} least-recently-seen photo(s) (never-seen first):");
            foreach (PhotoAuditRow r in rows)
            {
                sb.Append($"\n  {r.Id}  ·  last seen {FormatAge(r.LastSeenUtc, now)}  ·  added {FormatAge(r.FirstSeenUtc ?? r.ModifiedUtc, now)}  ·  {FormatBytes(r.SizeBytes)}");
            }
            return TextCommandResult.Success(sb.ToString());
        }

        private TextCommandResult OnDeleteOldest(TextCommandCallingArgs args)
        {
            PhotoDiskAuditService? svc = TryGetService(out string err);
            if (svc == null) return TextCommandResult.Error("photoadmin: " + err);

            int count = (int)args[0];
            bool confirm = IsConfirm(args[1]);
            DeletePlan plan = svc.PlanOldest(count, DateTime.UtcNow);
            return RunPlan(svc, plan, confirm, $"oldest {count}");
        }

        private TextCommandResult OnDeleteOlderThan(TextCommandCallingArgs args)
        {
            PhotoDiskAuditService? svc = TryGetService(out string err);
            if (svc == null) return TextCommandResult.Error("photoadmin: " + err);

            int days = (int)args[0];
            bool confirm = IsConfirm(args[1]);
            DeletePlan plan = svc.PlanOlderThan(days, DateTime.UtcNow);
            return RunPlan(svc, plan, confirm, $"older than {days}d");
        }

        private TextCommandResult OnDeleteById(TextCommandCallingArgs args)
        {
            PhotoDiskAuditService? svc = TryGetService(out string err);
            if (svc == null) return TextCommandResult.Error("photoadmin: " + err);

            string idsArg = (string)args[0];
            bool confirm = IsConfirm(args[1]);
            string[] ids = idsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            DeletePlan plan = svc.PlanByIds(ids, out List<string> missing, out List<string> ambiguous);
            return RunPlan(svc, plan, confirm, "by id", missing, ambiguous);
        }

        private TextCommandResult OnPruneIndex(TextCommandCallingArgs args)
        {
            PhotoDiskAuditService? svc = TryGetService(out string err);
            if (svc == null) return TextCommandResult.Error("photoadmin: " + err);

            bool confirm = IsConfirm(args[0]);
            int stale = svc.GetStats().StaleIndexCount;
            if (stale == 0) return TextCommandResult.Success("photoadmin: no stale index rows — nothing to prune.");

            if (!confirm)
                return TextCommandResult.Success(
                    $"photoadmin: DRY RUN — {stale} index row(s) have no backing file. Re-run with 'confirm' to prune.");

            int removed = svc.PruneIndex();
            _sapi.Logger.Notification($"photocore: /photoadmin pruned {removed} stale last-seen index row(s).");
            return TextCommandResult.Success($"photoadmin: pruned {removed} stale index row(s).");
        }

        private TextCommandResult RunPlan(PhotoDiskAuditService svc, DeletePlan plan, bool confirm, string label, IReadOnlyList<string>? missing = null, IReadOnlyList<string>? ambiguous = null)
        {
            var sb = new StringBuilder();
            if (missing != null && missing.Count > 0)
                sb.Append($"photoadmin: {missing.Count} id(s) not found and skipped: {string.Join(", ", missing)}\n");
            if (ambiguous != null && ambiguous.Count > 0)
                sb.Append($"photoadmin: {ambiguous.Count} fragment(s) matched multiple photos and were skipped (be more specific): {string.Join(", ", ambiguous)}\n");

            if (plan.IsEmpty)
            {
                sb.Append($"photoadmin: nothing to delete for '{label}'.");
                return TextCommandResult.Success(sb.ToString());
            }

            if (!confirm)
            {
                sb.Append($"photoadmin: DRY RUN — would delete {plan.Ids.Count} photo(s) ({FormatBytes(plan.TotalBytes)}), incl. {plan.NeverSeenCount} never-seen.");
                AppendIdSample(sb, plan.Ids);
                sb.Append("\n  Re-run with 'confirm' to execute. Deleted photos render blank on any plate still referencing them.");
                return TextCommandResult.Success(sb.ToString());
            }

            DeleteResult res = svc.Execute(plan);
            _sapi.Logger.Notification(
                $"photocore: /photoadmin '{label}' deleted {res.Deleted} photo(s), {res.BytesReclaimed} bytes reclaimed, {res.Failed} failed.");

            sb.Append($"photoadmin: deleted {res.Deleted} photo(s), reclaimed {FormatBytes(res.BytesReclaimed)}.");
            if (res.Failed > 0) sb.Append($" {res.Failed} could not be deleted (locked or in use).");
            return TextCommandResult.Success(sb.ToString());
        }

        private static void AppendIdSample(StringBuilder sb, IReadOnlyList<string> ids)
        {
            int shown = Math.Min(ids.Count, DryRunIdSampleLimit);
            for (int i = 0; i < shown; i++) sb.Append($"\n  {ids[i]}");
            if (ids.Count > shown) sb.Append($"\n  … and {ids.Count - shown} more");
        }

        private static bool IsConfirm(object? arg)
            => arg is string s && s.Equals("confirm", StringComparison.OrdinalIgnoreCase);

        // ---- develop whitelist ----

        private ExposureWhitelistService? TryGetWhitelist(out string error)
        {
            ExposureWhitelistService? wl = _owner.AdminToolingBridge.ExposureWhitelist;
            if (wl == null)
            {
                error = "the develop whitelist is not initialised yet (server still starting up).";
                return null;
            }
            error = string.Empty;
            return wl;
        }

        private TextCommandResult OnWhitelistStatus(TextCommandCallingArgs args)
        {
            ExposureWhitelistService? wl = TryGetWhitelist(out string err);
            if (wl == null) return TextCommandResult.Error("photoadmin: " + err);

            return TextCommandResult.Success(
                $"photoadmin: develop whitelist is {(wl.Enabled ? "ENABLED" : "disabled")} with {wl.Count} player(s)."
              + (wl.Enabled ? "" : " (disabled = everyone may develop)"));
        }

        private TextCommandResult OnWhitelistEnable(TextCommandCallingArgs args)
        {
            ExposureWhitelistService? wl = TryGetWhitelist(out string err);
            if (wl == null) return TextCommandResult.Error("photoadmin: " + err);

            bool changed = wl.SetEnabled(true);
            _owner.AdminToolingBridge.BroadcastDevelopPermission(_sapi);
            _sapi.Logger.Notification("photocore: /photoadmin develop whitelist enabled.");
            return TextCommandResult.Success(
                $"photoadmin: develop whitelist {(changed ? "enabled" : "already enabled")} — {wl.Count} player(s) allowed (operators always allowed).");
        }

        private TextCommandResult OnWhitelistDisable(TextCommandCallingArgs args)
        {
            ExposureWhitelistService? wl = TryGetWhitelist(out string err);
            if (wl == null) return TextCommandResult.Error("photoadmin: " + err);

            bool changed = wl.SetEnabled(false);
            _owner.AdminToolingBridge.BroadcastDevelopPermission(_sapi);
            _sapi.Logger.Notification("photocore: /photoadmin develop whitelist disabled.");
            return TextCommandResult.Success($"photoadmin: develop whitelist {(changed ? "disabled" : "already disabled")} — everyone may develop.");
        }

        private TextCommandResult OnWhitelistAdd(TextCommandCallingArgs args)
        {
            ExposureWhitelistService? wl = TryGetWhitelist(out string err);
            if (wl == null) return TextCommandResult.Error("photoadmin: " + err);

            string name = (string)args[0];
            if (!TryResolvePlayer(name, out string uid, out string resolvedName, out string resolveErr))
                return TextCommandResult.Error("photoadmin: " + resolveErr);

            bool added = wl.Add(uid, resolvedName);
            _owner.AdminToolingBridge.BroadcastDevelopPermission(_sapi);
            _sapi.Logger.Notification($"photocore: /photoadmin develop whitelist add {resolvedName} ({uid}).");
            return TextCommandResult.Success(
                added
                    ? $"photoadmin: added {resolvedName} to the develop whitelist ({wl.Count} player(s))."
                    : $"photoadmin: {resolvedName} was already on the develop whitelist.");
        }

        private TextCommandResult OnWhitelistRemove(TextCommandCallingArgs args)
        {
            ExposureWhitelistService? wl = TryGetWhitelist(out string err);
            if (wl == null) return TextCommandResult.Error("photoadmin: " + err);

            string arg = (string)args[0];

            // Escape hatch: accept a raw UID (as printed by `list`) so a member whose player-data was
            // purged or who was renamed can always be removed — name resolution can't reach them.
            string uid;
            string resolvedName;
            if (wl.Contains(arg))
            {
                uid = arg;
                resolvedName = wl.GetName(arg) ?? arg;
            }
            else if (!TryResolvePlayer(arg, out uid, out resolvedName, out string resolveErr))
            {
                return TextCommandResult.Error("photoadmin: " + resolveErr);
            }

            bool removed = wl.Remove(uid);
            _owner.AdminToolingBridge.BroadcastDevelopPermission(_sapi);
            if (removed) _sapi.Logger.Notification($"photocore: /photoadmin develop whitelist remove {resolvedName} ({uid}).");
            return TextCommandResult.Success(
                removed
                    ? $"photoadmin: removed {resolvedName} from the develop whitelist ({wl.Count} player(s))."
                    : $"photoadmin: {resolvedName} was not on the develop whitelist.");
        }

        private TextCommandResult OnWhitelistList(TextCommandCallingArgs args)
        {
            ExposureWhitelistService? wl = TryGetWhitelist(out string err);
            if (wl == null) return TextCommandResult.Error("photoadmin: " + err);

            IReadOnlyDictionary<string, string> players = wl.Snapshot();
            if (players.Count == 0)
                return TextCommandResult.Success("photoadmin: the develop whitelist is empty (only operators may develop while it is enabled).");

            var sb = new StringBuilder();
            sb.Append($"photoadmin: {players.Count} player(s) on the develop whitelist:");
            foreach (KeyValuePair<string, string> kvp in players)
                sb.Append($"\n  {kvp.Value}  ·  {kvp.Key}");
            return TextCommandResult.Success(sb.ToString());
        }

        private bool TryResolvePlayer(string name, out string uid, out string resolvedName, out string error)
        {
            uid = string.Empty;
            resolvedName = name;
            error = string.Empty;

            IPlayer? online = _sapi.World.AllOnlinePlayers
                .FirstOrDefault(p => string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase));
            if (online != null)
            {
                uid = online.PlayerUID;
                resolvedName = online.PlayerName;
                return true;
            }

            IServerPlayerData? data = _sapi.PlayerData.GetPlayerDataByLastKnownName(name);
            if (data != null)
            {
                uid = data.PlayerUID;
                resolvedName = data.LastKnownPlayername ?? name;
                return true;
            }

            error = $"no online or known player named '{name}'. Have them join once, or pass the exact last-seen name.";
            return false;
        }

        private static string FormatAge(DateTime? t, DateTime now)
        {
            if (t == null) return "never";
            TimeSpan d = now - t.Value;
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            if (d.TotalDays >= 1) return $"{(int)d.TotalDays}d {d.Hours}h ago";
            if (d.TotalHours >= 1) return $"{(int)d.TotalHours}h ago";
            if (d.TotalMinutes >= 1) return $"{(int)d.TotalMinutes}m ago";
            return "just now";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0.#} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.#} MB";
            return $"{mb / 1024.0:0.##} GB";
        }
    }
}
