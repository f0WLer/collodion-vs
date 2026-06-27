using Xunit;
using Photocore.PhotoSync.Runtime;

namespace Photocore.Tests.PhotoSync;

public class ServerExpectedUploadsTests
{
    [Fact]
    public void RegisteredUploadIsExpected()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);
        uploads.Register("player1", "photo1.png", nowMs: 0);

        Assert.True(uploads.IsExpected("player1", "photo1.png", nowMs: 0));
    }

    [Fact]
    public void UnregisteredUploadIsNotExpected()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);

        Assert.False(uploads.IsExpected("player1", "photo1.png", nowMs: 0));
    }

    [Fact]
    public void ExpiredEntryIsNotExpected()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 10_000);
        uploads.Register("player1", "photo1.png", nowMs: 0);

        Assert.False(uploads.IsExpected("player1", "photo1.png", nowMs: 20_000));
    }

    [Fact]
    public void ConsumedEntryIsNotExpected()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);
        uploads.Register("player1", "photo1.png", nowMs: 0);
        uploads.Consume("player1", "photo1.png");

        Assert.False(uploads.IsExpected("player1", "photo1.png", nowMs: 0));
    }

    [Fact]
    public void UploadIsolatedByPlayer()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);
        uploads.Register("player1", "photo1.png", nowMs: 0);

        Assert.False(uploads.IsExpected("player2", "photo1.png", nowMs: 0));
    }

    [Fact]
    public void TryBeginUploadRespectsPlayerCap()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);

        Assert.True(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 2));
        Assert.True(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 2));
        Assert.False(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 2));
    }

    [Fact]
    public void EndUploadFreesSlot()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);

        uploads.TryBeginUpload("player1", maxOpenPerPlayer: 1);
        Assert.False(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 1));

        uploads.EndUpload("player1");
        Assert.True(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 1));
    }

    [Fact]
    public void PlayerCapsAreIndependent()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);

        uploads.TryBeginUpload("player1", maxOpenPerPlayer: 1);
        Assert.False(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 1));

        // player2 has their own counter.
        Assert.True(uploads.TryBeginUpload("player2", maxOpenPerPlayer: 1));
    }

    [Fact]
    public void RejectsNullOrEmptyInputs()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);

        uploads.Register("", "photo1.png", nowMs: 0);
        uploads.Register("player1", "", nowMs: 0);

        Assert.False(uploads.IsExpected("", "photo1.png", nowMs: 0));
        Assert.False(uploads.IsExpected("player1", "", nowMs: 0));
    }

    [Fact]
    public void EndUploadWithoutBeginHasNoEffect()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);

        // Should not throw or corrupt counter state.
        uploads.EndUpload("player1");
        uploads.EndUpload("player1");

        // After spurious ends, normal cap should still apply.
        Assert.True(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 1));
        Assert.False(uploads.TryBeginUpload("player1", maxOpenPerPlayer: 1));
    }

    [Fact]
    public void ReRegisterRefreshesExpiry()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 10_000);
        uploads.Register("player1", "photo1.png", nowMs: 0);

        // Re-register at t=8_000 — new expiry is t=18_000.
        uploads.Register("player1", "photo1.png", nowMs: 8_000);

        // At t=12_000 the original TTL would have expired, but refreshed entry is still valid.
        Assert.True(uploads.IsExpected("player1", "photo1.png", nowMs: 12_000));
        Assert.False(uploads.IsExpected("player1", "photo1.png", nowMs: 20_000));
    }

    [Fact]
    public void ExpiredEntriesArePrunedOnRegister()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 10_000);

        // Register 100 entries at t=0, all expire at t=10_000.
        for (int i = 0; i < 100; i++)
            uploads.Register($"player{i}", "photo.png", nowMs: 0);

        // At t=60_000 (past prune interval), register one new entry — triggers prune.
        uploads.Register("newplayer", "photo.png", nowMs: 60_000);

        // All original entries should now be expired and removed.
        for (int i = 0; i < 100; i++)
            Assert.False(uploads.IsExpected($"player{i}", "photo.png", nowMs: 60_000));

        Assert.True(uploads.IsExpected("newplayer", "photo.png", nowMs: 60_000));
    }

    [Fact]
    public async Task ConcurrentBeginUploadRespectsCapacity()
    {
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);
        int cap = 5;
        int successCount = 0;

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            if (uploads.TryBeginUpload("player1", maxOpenPerPlayer: cap))
                Interlocked.Increment(ref successCount);
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(cap, successCount);
    }

    [Fact]
    public async Task ConcurrentMultiPlayerRegisterAndConsumeIsConsistent()
    {
        // 200 players each register one upload, then consume it concurrently.
        // After all tasks complete every entry should be gone — no phantom entries, no corruption.
        var uploads = new ServerExpectedUploads(ttlMs: 60_000);
        int playerCount = 200;

        var registerTasks = Enumerable.Range(0, playerCount).Select(i => Task.Run(() =>
            uploads.Register($"player{i}", "photo.png", nowMs: 0))).ToArray();

        await Task.WhenAll(registerTasks);

        var consumeTasks = Enumerable.Range(0, playerCount).Select(i => Task.Run(() =>
            uploads.Consume($"player{i}", "photo.png"))).ToArray();

        await Task.WhenAll(consumeTasks);

        for (int i = 0; i < playerCount; i++)
            Assert.False(uploads.IsExpected($"player{i}", "photo.png", nowMs: 0));
    }
}
