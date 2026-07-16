using System.Globalization;
using Photocore.PhotoMetadata.Model;
using Photocore.PhotoSync;

namespace Photocore.PhotoMetadata
{
    internal readonly struct DeleteResult
    {
        internal readonly int Deleted;
        internal readonly long BytesReclaimed;
        internal readonly int Failed;

        internal DeleteResult(int deleted, long bytesReclaimed, int failed)
        {
            Deleted = deleted;
            BytesReclaimed = bytesReclaimed;
            Failed = failed;
        }
    }

    // Server-side orchestration for the /photoadmin disk audit. Reconciles the on-disk source-photo set
    // with the last-seen index, drives the pure selection logic in PhotoAuditLogic, and executes deletes
    // (cascading derived masks + the index row). All IO is best-effort; the pure logic is what's tested.
    internal sealed class PhotoDiskAuditService
    {
        private readonly ServerPhotoSeenService _seen;
        private readonly double _graceHours;

        internal PhotoDiskAuditService(ServerPhotoSeenService seen, double graceHours)
        {
            _seen = seen;
            _graceHours = graceHours <= 0 ? 0 : graceHours;
        }

        internal double GraceHours => _graceHours;

        // Joins the on-disk photo files with the index snapshot into audit rows, and reports the index
        // ids that have no backing file (stale rows) via [staleIndexIds].
        private List<PhotoAuditRow> BuildRows(out List<string> staleIndexIds)
        {
            IReadOnlyDictionary<string, PhotoLastSeenEntry> index = _seen.SnapshotEntries();
            IReadOnlyList<string> diskIds = PhotoAssetStoragePaths.EnumeratePhotoIds();
            var diskSet = new HashSet<string>(diskIds, StringComparer.OrdinalIgnoreCase);

            var rows = new List<PhotoAuditRow>(diskIds.Count);
            foreach (string id in diskIds)
            {
                index.TryGetValue(id, out PhotoLastSeenEntry? entry);
                rows.Add(new PhotoAuditRow(
                    id,
                    PhotoAssetStoragePaths.GetPhotoSizeBytes(id),
                    ParseUtc(entry?.LastSeenUtc),
                    ParseUtc(entry?.FirstSeenUtc),
                    PhotoAssetStoragePaths.GetPhotoModifiedUtc(id)));
            }

            staleIndexIds = new List<string>();
            foreach (string id in index.Keys)
            {
                if (!diskSet.Contains(id)) staleIndexIds.Add(id);
            }

            return rows;
        }

        private static DateTime? ParseUtc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt))
                return dt.ToUniversalTime();
            return null;
        }

        internal AuditStats GetStats()
        {
            List<PhotoAuditRow> rows = BuildRows(out List<string> stale);
            return PhotoAuditLogic.ComputeStats(rows, stale.Count);
        }

        internal IReadOnlyList<PhotoAuditRow> GetAudit(int count)
        {
            List<PhotoAuditRow> rows = BuildRows(out _);
            return PhotoAuditLogic.BuildAudit(rows, count);
        }

        internal DeletePlan PlanOldest(int count, DateTime nowUtc)
        {
            List<PhotoAuditRow> rows = BuildRows(out _);
            return PhotoAuditLogic.PlanOldest(rows, count, nowUtc, _graceHours);
        }

        internal DeletePlan PlanOlderThan(double days, DateTime nowUtc)
        {
            List<PhotoAuditRow> rows = BuildRows(out _);
            return PhotoAuditLogic.PlanOlderThan(rows, days, nowUtc, _graceHours);
        }

        internal DeletePlan PlanByIds(IEnumerable<string> ids, out List<string> missing, out List<string> ambiguous)
        {
            List<PhotoAuditRow> rows = BuildRows(out _);
            return PhotoAuditLogic.PlanByIds(rows, ids, out missing, out ambiguous);
        }

        // Executes a delete plan: removes each source photo + its derived masks, and drops the index row.
        // Per-id size is read just before deletion so the reclaimed total reflects what was actually freed.
        // A source that has already vanished (e.g. deleted out-of-band between planning and execution) is
        // treated as success with 0 bytes — the admin's intent was "make it gone", and the index row is
        // still dropped. Only a file that is still present after a failed delete counts as Failed.
        internal DeleteResult Execute(DeletePlan plan)
        {
            int deleted = 0, failed = 0;
            long bytes = 0;
            foreach (string id in plan.Ids)
            {
                // Ids here always come from EnumeratePhotoIds (scoped-only), so this always resolves
                // to the scoped path in practice; TryResolveReadPath is used for consistency with the
                // rest of the read surface, not because a legacy hit is expected here.
                string sourcePath = PhotoAssetStoragePaths.TryResolveReadPath(id);
                long size = PhotoAssetStoragePaths.GetPhotoSizeBytes(id);
                bool removed = PhotoAssetStoragePaths.DeletePhotoAndDerived(id);

                if (removed || !File.Exists(sourcePath))
                {
                    deleted++;
                    bytes += size; // 0 if it was already gone
                    _seen.RemoveEntry(id); // harmless no-op for never-seen photos with no index row
                }
                else
                {
                    failed++; // source still on disk → genuinely couldn't delete (locked / in use)
                }
            }
            return new DeleteResult(deleted, bytes, failed);
        }

        // Drops index rows whose backing source file no longer exists. Returns the number removed.
        // Uses GetPhotoPath so the existence check normalizes the id the same way the rest of the
        // pipeline (and the file writer) does, rather than checking a raw, possibly non-canonical key.
        internal int PruneIndex()
            => _seen.RemoveEntriesWithoutFile(id => File.Exists(PhotoAssetStoragePaths.GetPhotoPath(id)));
    }
}
