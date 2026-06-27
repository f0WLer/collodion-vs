using Xunit;
using Photocore.PhotoMetadata;

namespace Photocore.Tests.PhotoMetadata;

public class PhotoAuditLogicTests
{
    private static readonly DateTime _now = new DateTime(2026, 06, 23, 12, 0, 0, DateTimeKind.Utc);

    private static PhotoAuditRow Seen(string id, DateTime lastSeen, DateTime? firstSeen = null, long size = 100)
        => new PhotoAuditRow(id, size, lastSeen, firstSeen ?? lastSeen, lastSeen);

    private static PhotoAuditRow NeverSeen(string id, DateTime modified, long size = 100)
        => new PhotoAuditRow(id, size, null, null, modified);

    // ---- grace ----

    [Fact]
    public void PassesGraceWhenFirstSeenOlderThanFloor()
    {
        var row = Seen("a.png", _now.AddDays(-10), firstSeen: _now.AddDays(-30));
        Assert.True(PhotoAuditLogic.PassesGrace(row, _now, graceHours: 24));
    }

    [Fact]
    public void FailsGraceWhenFirstSeenInsideFloor()
    {
        var row = Seen("a.png", _now.AddHours(-1), firstSeen: _now.AddHours(-1));
        Assert.False(PhotoAuditLogic.PassesGrace(row, _now, graceHours: 24));
    }

    [Fact]
    public void NeverSeenUsesMtimeForGraceFallback()
    {
        var fresh = NeverSeen("a.png", _now.AddHours(-1));
        var old = NeverSeen("b.png", _now.AddDays(-5));
        Assert.False(PhotoAuditLogic.PassesGrace(fresh, _now, 24));
        Assert.True(PhotoAuditLogic.PassesGrace(old, _now, 24));
    }

    [Fact]
    public void IndeterminateAgeFailsGrace()
    {
        var row = new PhotoAuditRow("a.png", 100, lastSeenUtc: null, firstSeenUtc: null, modifiedUtc: null);
        Assert.False(PhotoAuditLogic.PassesGrace(row, _now, 24));
    }

    // ---- ordering ----

    [Fact]
    public void OrdersNeverSeenBeforeSeenThenByAge()
    {
        var rows = new List<PhotoAuditRow>
        {
            Seen("seen-recent.png", _now.AddDays(-1)),
            Seen("seen-old.png", _now.AddDays(-10)),
            NeverSeen("never-new.png", _now.AddDays(-2)),
            NeverSeen("never-old.png", _now.AddDays(-20)),
        };

        var ordered = PhotoAuditLogic.OrderLeastRecentlySeen(rows).Select(r => r.Id).ToList();

        Assert.Equal(
            new[] { "never-old.png", "never-new.png", "seen-old.png", "seen-recent.png" },
            ordered);
    }

    // ---- oldest ----

    [Fact]
    public void PlanOldestTakesCountGraceFilteredNeverSeenFirst()
    {
        var rows = new List<PhotoAuditRow>
        {
            NeverSeen("never-old.png", _now.AddDays(-20), size: 10),
            Seen("seen-old.png", _now.AddDays(-10), size: 20),
            Seen("seen-fresh.png", _now.AddHours(-2), firstSeen: _now.AddHours(-2), size: 40), // inside grace
        };

        DeletePlan plan = PhotoAuditLogic.PlanOldest(rows, count: 2, _now, graceHours: 24);

        Assert.Equal(new[] { "never-old.png", "seen-old.png" }, plan.Ids.ToArray());
        Assert.Equal(30, plan.TotalBytes);
        Assert.Equal(1, plan.NeverSeenCount);
    }

    [Fact]
    public void PlanOldestZeroOrNegativeCountIsEmpty()
    {
        var rows = new List<PhotoAuditRow> { Seen("a.png", _now.AddDays(-10)) };
        Assert.True(PhotoAuditLogic.PlanOldest(rows, 0, _now, 24).IsEmpty);
        Assert.True(PhotoAuditLogic.PlanOldest(rows, -3, _now, 24).IsEmpty);
    }

    // ---- olderthan ----

    [Fact]
    public void PlanOlderThanIncludesNeverSeenPastGraceExcludesRecentlySeen()
    {
        var rows = new List<PhotoAuditRow>
        {
            Seen("dormant.png", _now.AddDays(-90)),
            Seen("active.png", _now.AddDays(-3)),
            NeverSeen("orphan.png", _now.AddDays(-90)),
        };

        DeletePlan plan = PhotoAuditLogic.PlanOlderThan(rows, days: 30, _now, graceHours: 24);

        Assert.Contains("dormant.png", plan.Ids);
        Assert.Contains("orphan.png", plan.Ids);
        Assert.DoesNotContain("active.png", plan.Ids);
    }

    [Fact]
    public void PlanOlderThanExcludesNeverSeenInsideGrace()
    {
        var rows = new List<PhotoAuditRow>
        {
            NeverSeen("brand-new.png", _now.AddHours(-1)),
        };

        DeletePlan plan = PhotoAuditLogic.PlanOlderThan(rows, days: 30, _now, graceHours: 24);

        Assert.True(plan.IsEmpty);
    }

    // ---- by id ----

    [Fact]
    public void PlanByIdsMatchesNormalizedReportsMissingBypassesGrace()
    {
        var rows = new List<PhotoAuditRow>
        {
            // Fresh (would fail grace) but explicitly named, so it must still be selected.
            Seen("keep.png", _now.AddHours(-1), firstSeen: _now.AddHours(-1), size: 50),
        };

        // "keep" normalizes to "keep.png"; "gone.png" is absent; "../bad" normalizes to empty.
        DeletePlan plan = PhotoAuditLogic.PlanByIds(rows, new[] { "keep", "gone.png", "../bad" }, out List<string> missing);

        Assert.Equal(new[] { "keep.png" }, plan.Ids.ToArray());
        Assert.Equal(50, plan.TotalBytes);
        Assert.Contains("gone.png", missing);
        Assert.Contains("../bad", missing);
    }

    // ---- audit + stats ----

    [Fact]
    public void BuildAuditOrdersLeastRecentlySeenNoGraceFilterCapsCount()
    {
        var rows = new List<PhotoAuditRow>
        {
            Seen("a.png", _now.AddHours(-1)),     // recent — would fail grace, but audit shows it
            Seen("b.png", _now.AddDays(-10)),
            NeverSeen("c.png", _now.AddHours(-1)),
        };

        var ids = PhotoAuditLogic.BuildAudit(rows, count: 2).Select(r => r.Id).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal("c.png", ids[0]); // never-seen first even though recent
        Assert.Equal("b.png", ids[1]);
    }

    [Fact]
    public void ComputeStatsBucketsAndBytes()
    {
        var rows = new List<PhotoAuditRow>
        {
            Seen("a.png", _now.AddDays(-1), size: 100),
            Seen("b.png", _now.AddDays(-2), size: 200),
            NeverSeen("c.png", _now.AddDays(-3), size: 50),
        };

        AuditStats stats = PhotoAuditLogic.ComputeStats(rows, staleIndexCount: 4);

        Assert.Equal(3, stats.TotalFiles);
        Assert.Equal(350, stats.TotalBytes);
        Assert.Equal(2, stats.SeenCount);
        Assert.Equal(300, stats.SeenBytes);
        Assert.Equal(1, stats.NeverSeenCount);
        Assert.Equal(50, stats.NeverSeenBytes);
        Assert.Equal(4, stats.StaleIndexCount);
    }
}
