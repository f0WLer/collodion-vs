using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Photochemistry.PhotoMetadata;

namespace Photochemistry.AdminTooling
{
    // Server-side operator commands for auditing and reclaiming the on-disk photo store, driven by the
    // last-seen index (see DESIGN-photo-disk-audit). Registered under /photoadmin, gated on controlserver.
    // Dry-run by default; the literal word 'confirm' executes a destructive action.
    internal sealed class ServerPhotoCommands
    {
        private const int DryRunIdSampleLimit = 15;

        private readonly ICoreServerAPI _sapi;
        private readonly PhotochemistryModSystem _owner;

        private ServerPhotoCommands(ICoreServerAPI sapi, PhotochemistryModSystem owner)
        {
            _sapi = sapi;
            _owner = owner;
        }

        internal static void Register(ICoreServerAPI sapi, PhotochemistryModSystem owner)
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
            double grace = PhotochemistryConfigAccess.ResolveConfig(_sapi)?.PhotoSync?.PhotoDeleteGraceHours ?? 24.0;
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

            DeletePlan plan = svc.PlanByIds(ids, out List<string> missing);
            return RunPlan(svc, plan, confirm, "by id", missing);
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
            _sapi.Logger.Notification($"photochemistry: /photoadmin pruned {removed} stale last-seen index row(s).");
            return TextCommandResult.Success($"photoadmin: pruned {removed} stale index row(s).");
        }

        private TextCommandResult RunPlan(PhotoDiskAuditService svc, DeletePlan plan, bool confirm, string label, IReadOnlyList<string>? missing = null)
        {
            var sb = new StringBuilder();
            if (missing != null && missing.Count > 0)
                sb.Append($"photoadmin: {missing.Count} id(s) not found and skipped: {string.Join(", ", missing)}\n");

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
                $"photochemistry: /photoadmin '{label}' deleted {res.Deleted} photo(s), {res.BytesReclaimed} bytes reclaimed, {res.Failed} failed.");

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
