using Xunit;
using Photochemistry.PhotoSync.Runtime;

namespace Photochemistry.Tests.PhotoSync;

public class PlayerNetworkThrottleTests
{
    [Fact]
    public void AllowsRequestsWithinBurst()
    {
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 3);
        long now = 0;

        Assert.True(throttle.TryConsume("player1", "req", now));
        Assert.True(throttle.TryConsume("player1", "req", now));
        Assert.True(throttle.TryConsume("player1", "req", now));
    }

    [Fact]
    public void BlocksRequestsExceedingBurst()
    {
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 3);
        long now = 0;

        throttle.TryConsume("player1", "req", now);
        throttle.TryConsume("player1", "req", now);
        throttle.TryConsume("player1", "req", now);

        Assert.False(throttle.TryConsume("player1", "req", now));
    }

    [Fact]
    public void RefillsTokensOverTime()
    {
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 1);
        long now = 0;

        Assert.True(throttle.TryConsume("player1", "req", now));
        Assert.False(throttle.TryConsume("player1", "req", now));

        // 1 permit per second (60/min), so after 1000ms we should have 1 token back.
        Assert.True(throttle.TryConsume("player1", "req", now + 1_000));
    }

    [Fact]
    public void ScopesAreIsolatedPerPlayer()
    {
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 1);
        long now = 0;

        Assert.True(throttle.TryConsume("player1", "req", now));
        Assert.False(throttle.TryConsume("player1", "req", now));

        // player2 has their own bucket — should still have tokens.
        Assert.True(throttle.TryConsume("player2", "req", now));
    }

    [Fact]
    public void ScopesAreIsolatedByScopeKey()
    {
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 1);
        long now = 0;

        Assert.True(throttle.TryConsume("player1", "req", now));
        Assert.False(throttle.TryConsume("player1", "req", now));

        // Different scope key — separate bucket.
        Assert.True(throttle.TryConsume("player1", "upload", now));
    }

    [Fact]
    public void RejectsEmptyPlayerUid()
    {
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 8);
        Assert.False(throttle.TryConsume("", "req", 0));
        Assert.False(throttle.TryConsume(null!, "req", 0));
    }

    [Fact]
    public void ManyPlayersEachGetOwnBucket()
    {
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 3);
        long now = 0;

        // 200 distinct players should each get their full burst capacity.
        for (int i = 0; i < 200; i++)
        {
            string player = $"player{i}";
            Assert.True(throttle.TryConsume(player, "req", now));
            Assert.True(throttle.TryConsume(player, "req", now));
            Assert.True(throttle.TryConsume(player, "req", now));
            Assert.False(throttle.TryConsume(player, "req", now));
        }
    }

    [Fact]
    public void IdleBucketsArePrunedAndFreshBucketIssuedOnReturn()
    {
        // PruneIntervalMs = 60_000, IdleEvictMs = 300_000 (5 min) — both internal constants.
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 60, burstCapacity: 3);

        // Exhaust player1's burst at t=0.
        throttle.TryConsume("player1", "req", 0);
        throttle.TryConsume("player1", "req", 0);
        throttle.TryConsume("player1", "req", 0);
        Assert.False(throttle.TryConsume("player1", "req", 0));

        // Advance past IdleEvictMs (300_000ms). player2 call triggers prune, evicting player1's idle bucket.
        throttle.TryConsume("player2", "req", 400_000);

        // player1's bucket was evicted — next call gets a fresh bucket and succeeds.
        Assert.True(throttle.TryConsume("player1", "req", 400_000));
    }

    [Fact]
    public async Task ConcurrentConsumeDoesNotExceedBurstCapacity()
    {
        int burst = 10;
        var throttle = new PlayerNetworkThrottle(permitsPerMinute: 6000, burstCapacity: burst);
        long now = 0;
        int successCount = 0;

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            if (throttle.TryConsume("player1", "req", now))
                Interlocked.Increment(ref successCount);
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(burst, successCount);
    }
}
