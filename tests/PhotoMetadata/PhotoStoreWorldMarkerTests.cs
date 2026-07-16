using Xunit;
using Photocore.PhotoMetadata;

namespace Photocore.Tests.PhotoMetadata;

// Uses a real throwaway temp directory rather than mocking the filesystem -- CheckAndUpdate's whole
// job is a two-line marker file read/compare/write, so a temp dir keeps the test both simple and
// faithful. Each test gets its own directory (constructor/Dispose run per test), so no cleanup
// coordination is needed between tests.
public class PhotoStoreWorldMarkerTests : IDisposable
{
    private readonly string _storeDir;

    public PhotoStoreWorldMarkerTests()
    {
        _storeDir = Path.Combine(Path.GetTempPath(), "photocore-marker-test-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_storeDir, true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void FirstUseIsNotSharedAndWritesTheMarker()
    {
        bool shared = PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"C:\saves\world.vcdbs", "My World");

        Assert.False(shared);
        Assert.True(File.Exists(Path.Combine(_storeDir, "world.txt")));
    }

    [Fact]
    public void SameWorldPathOnLaterRunsIsNotShared()
    {
        PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"C:\saves\world.vcdbs", "My World");
        bool shared = PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"C:\saves\world.vcdbs", "My World");

        Assert.False(shared);
    }

    [Fact]
    public void DifferentWorldPathIsFlaggedShared()
    {
        PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"C:\saves\world.vcdbs", "My World");
        bool shared = PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"D:\copies\world-copy.vcdbs", "My World Copy");

        Assert.True(shared);
    }

    [Fact]
    public void MarkerIsOverwrittenWithTheLatestWorldAfterAMismatch()
    {
        PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"C:\saves\world.vcdbs", "My World");
        PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"D:\copies\world-copy.vcdbs", "My World Copy");

        // Reconnecting to the SAME copy again should no longer read as shared against itself.
        bool shared = PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"D:\copies\world-copy.vcdbs", "My World Copy");
        Assert.False(shared);
    }

    [Fact]
    public void CreatesTheStoreDirectoryIfMissing()
    {
        Assert.False(Directory.Exists(_storeDir));
        PhotoStoreWorldMarker.CheckAndUpdate(_storeDir, @"C:\saves\world.vcdbs", "My World");
        Assert.True(Directory.Exists(_storeDir));
    }
}
