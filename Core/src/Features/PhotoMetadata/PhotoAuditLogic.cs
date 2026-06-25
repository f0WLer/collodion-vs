using Photochemistry.PhotoSync.Storage;

namespace Photochemistry.PhotoMetadata
{
    // A single on-disk source photo joined with its last-seen index data, for audit and selection.
    // Pure data — no filesystem access — so the selection logic below is unit-testable.
    internal readonly struct PhotoAuditRow
    {
        internal readonly string Id;
        internal readonly long SizeBytes;
        internal readonly DateTime? LastSeenUtc;   // null = never requested by any client
        internal readonly DateTime? FirstSeenUtc;  // null = no index row at all
        internal readonly DateTime? ModifiedUtc;   // file mtime; grace fallback for never-seen files

        internal PhotoAuditRow(string id, long sizeBytes, DateTime? lastSeenUtc, DateTime? firstSeenUtc, DateTime? modifiedUtc)
        {
            Id = id;
            SizeBytes = sizeBytes;
            LastSeenUtc = lastSeenUtc;
            FirstSeenUtc = firstSeenUtc;
            ModifiedUtc = modifiedUtc;
        }

        internal bool NeverSeen => LastSeenUtc == null;
    }

    internal readonly struct DeletePlan
    {
        internal readonly IReadOnlyList<string> Ids;
        internal readonly long TotalBytes;
        internal readonly int NeverSeenCount;

        internal DeletePlan(IReadOnlyList<string> ids, long totalBytes, int neverSeenCount)
        {
            Ids = ids;
            TotalBytes = totalBytes;
            NeverSeenCount = neverSeenCount;
        }

        internal bool IsEmpty => Ids.Count == 0;

        internal static DeletePlan From(IReadOnlyList<PhotoAuditRow> rows)
        {
            var ids = new List<string>(rows.Count);
            long bytes = 0;
            int never = 0;
            foreach (PhotoAuditRow r in rows)
            {
                ids.Add(r.Id);
                bytes += r.SizeBytes;
                if (r.NeverSeen) never++;
            }
            return new DeletePlan(ids, bytes, never);
        }
    }

    // Summary of the on-disk photo store reconciled against the index.
    internal readonly struct AuditStats
    {
        internal readonly int TotalFiles;
        internal readonly long TotalBytes;
        internal readonly int SeenCount;
        internal readonly long SeenBytes;
        internal readonly int NeverSeenCount;
        internal readonly long NeverSeenBytes;
        internal readonly int StaleIndexCount;

        internal AuditStats(int totalFiles, long totalBytes, int seenCount, long seenBytes,
            int neverSeenCount, long neverSeenBytes, int staleIndexCount)
        {
            TotalFiles = totalFiles;
            TotalBytes = totalBytes;
            SeenCount = seenCount;
            SeenBytes = seenBytes;
            NeverSeenCount = neverSeenCount;
            NeverSeenBytes = neverSeenBytes;
            StaleIndexCount = staleIndexCount;
        }
    }

    // Pure disk-audit selection logic over PhotoAuditRow sets. No filesystem or VS API access, so the
    // bucketing/ordering/selection rules are unit-testable with hand-built rows and a fixed clock.
    internal static class PhotoAuditLogic
    {
        // Reference time used for the grace floor: prefer the index's first-seen, fall back to file mtime.
        private static DateTime? GraceReference(in PhotoAuditRow r) => r.FirstSeenUtc ?? r.ModifiedUtc;

        // A row is grace-eligible only when we can establish it is older than (now - graceHours).
        // An indeterminate age (no first-seen and no mtime) is conservatively treated as NOT eligible,
        // so a brand-new or unreadable file is never auto-deleted by an age/count selector.
        internal static bool PassesGrace(in PhotoAuditRow r, DateTime nowUtc, double graceHours)
        {
            DateTime? reference = GraceReference(r);
            if (reference == null) return false;
            return reference.Value <= nowUtc.AddHours(-graceHours);
        }

        // Orders rows least-recently-seen first: the never-seen group (oldest file mtime first) ahead of
        // the seen group (oldest last-seen first). Never-seen photos are the strongest orphan candidates.
        internal static IEnumerable<PhotoAuditRow> OrderLeastRecentlySeen(IEnumerable<PhotoAuditRow> rows)
            => rows
                .OrderBy(r => r.NeverSeen ? 0 : 1)
                .ThenBy(r => r.NeverSeen ? (r.ModifiedUtc ?? DateTime.MinValue) : (r.LastSeenUtc ?? DateTime.MinValue))
                .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase);

        internal static DeletePlan PlanOldest(IReadOnlyList<PhotoAuditRow> rows, int count, DateTime nowUtc, double graceHours)
        {
            if (count <= 0) return DeletePlan.From(Array.Empty<PhotoAuditRow>());

            List<PhotoAuditRow> selected = OrderLeastRecentlySeen(rows.Where(r => PassesGrace(r, nowUtc, graceHours)))
                .Take(count)
                .ToList();
            return DeletePlan.From(selected);
        }

        // Every photo not seen in the last [days], grace-filtered. Never-seen files are included once
        // past the grace floor (they are the strongest orphans), matching the design decision.
        internal static DeletePlan PlanOlderThan(IReadOnlyList<PhotoAuditRow> rows, double days, DateTime nowUtc, double graceHours)
        {
            DateTime threshold = nowUtc.AddDays(-days);
            List<PhotoAuditRow> selected = OrderLeastRecentlySeen(
                    rows.Where(r => PassesGrace(r, nowUtc, graceHours)
                                 && (r.LastSeenUtc == null || r.LastSeenUtc.Value < threshold)))
                .ToList();
            return DeletePlan.From(selected);
        }

        // Specific ids (already normalized by the caller's parse). Explicit admin selection, so grace is
        // intentionally bypassed. Ids with no matching on-disk file are reported via [missing].
        internal static DeletePlan PlanByIds(IReadOnlyList<PhotoAuditRow> rows, IEnumerable<string> requestedIds, out List<string> missing)
        {
            var byId = rows.ToDictionary(r => r.Id, r => r, StringComparer.OrdinalIgnoreCase);
            var matched = new List<PhotoAuditRow>();
            missing = new List<string>();

            foreach (string raw in requestedIds)
            {
                string id = PhotoAssetStoragePaths.NormalizePhotoId(raw);
                if (string.IsNullOrEmpty(id)) { missing.Add(raw); continue; }
                if (byId.TryGetValue(id, out PhotoAuditRow row)) matched.Add(row);
                else missing.Add(id);
            }
            return DeletePlan.From(matched);
        }

        // The audit listing: least-recently-seen first, capped at [count]. No grace filter — the audit
        // shows everything, including recent photos, so the admin sees the full picture before acting.
        internal static IReadOnlyList<PhotoAuditRow> BuildAudit(IReadOnlyList<PhotoAuditRow> rows, int count)
            => OrderLeastRecentlySeen(rows).Take(Math.Max(0, count)).ToList();

        internal static AuditStats ComputeStats(IReadOnlyList<PhotoAuditRow> rows, int staleIndexCount)
        {
            long totalBytes = 0, seenBytes = 0, neverBytes = 0;
            int seen = 0, never = 0;
            foreach (PhotoAuditRow r in rows)
            {
                totalBytes += r.SizeBytes;
                if (r.NeverSeen) { never++; neverBytes += r.SizeBytes; }
                else { seen++; seenBytes += r.SizeBytes; }
            }
            return new AuditStats(rows.Count, totalBytes, seen, seenBytes, never, neverBytes, staleIndexCount);
        }
    }
}
