using Xunit;
using Vintagestory.API.Config;
using Photocore.PhotoSync;

namespace Photocore.Tests.PhotoSync;

// Redirects GamePaths.DataPath to a throwaway temp folder for the duration of each test so these
// exercise real file existence checks without touching the real game data folder. xUnit creates a
// fresh instance (constructor + Dispose) per test method, so static state never leaks between tests.
public class PhotoAssetStoragePathsScopingTests : IDisposable
{
    private readonly string _originalDataPath;
    private readonly string _tempDataPath;

    public PhotoAssetStoragePathsScopingTests()
    {
        _originalDataPath = GamePaths.DataPath;
        _tempDataPath = Path.Combine(Path.GetTempPath(), "photocore-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataPath);
        GamePaths.DataPath = _tempDataPath;
    }

    public void Dispose()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(null);
        GamePaths.DataPath = _originalDataPath;
        try { Directory.Delete(_tempDataPath, true); } catch { /* best-effort cleanup */ }
    }

    private string LegacyPhotosDirectory => Path.Combine(_tempDataPath, "ModData", "photocore", "photos");

    // ---- SanitizeScopeFolderName ----

    [Theory]
    [InlineData("../secret")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeRejectsUnsafeScopeIds(string input)
    {
        Assert.Equal(string.Empty, PhotoAssetStoragePaths.SanitizeScopeFolderName(input));
    }

    [Fact]
    public void SanitizePassesThroughAGuid()
    {
        string id = Guid.NewGuid().ToString();
        Assert.Equal(id, PhotoAssetStoragePaths.SanitizeScopeFolderName(id));
    }

    // ---- GetPhotosDirectory scoping ----

    [Fact]
    public void UnscopedFallsBackToFlatLegacyRoot()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(null);
        Assert.Equal(LegacyPhotosDirectory, PhotoAssetStoragePaths.GetPhotosDirectory());
    }

    [Fact]
    public void ScopedAppendsTheWorldId()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-a");
        Assert.Equal(Path.Combine(LegacyPhotosDirectory, "world-a"), PhotoAssetStoragePaths.GetPhotosDirectory());
    }

    [Fact]
    public void EmptyProviderResultFallsBackToFlatRoot()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "");
        Assert.Equal(LegacyPhotosDirectory, PhotoAssetStoragePaths.GetPhotosDirectory());
    }

    // ---- TryResolveReadPath scoped-then-legacy resolution ----

    [Fact]
    public void ReadResolvesScopedFileWhenPresent()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-b");
        string scopedDir = PhotoAssetStoragePaths.GetPhotosDirectory();
        Directory.CreateDirectory(scopedDir);
        string scopedFile = Path.Combine(scopedDir, "exposure_abc12345.png");
        File.WriteAllBytes(scopedFile, new byte[] { 1 });

        Assert.Equal(scopedFile, PhotoAssetStoragePaths.TryResolveReadPath("exposure_abc12345"));
    }

    [Fact]
    public void ReadFallsBackToLegacyFlatRootWhenNotInScopedFolder()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-c");
        Directory.CreateDirectory(LegacyPhotosDirectory);
        string legacyFile = Path.Combine(LegacyPhotosDirectory, "exposure_legacy01.png");
        File.WriteAllBytes(legacyFile, new byte[] { 1 });

        Assert.Equal(legacyFile, PhotoAssetStoragePaths.TryResolveReadPath("exposure_legacy01"));
    }

    [Fact]
    public void ReadReturnsScopedPathAsCanonicalWhenMissingEverywhere()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-d");
        string expected = Path.Combine(PhotoAssetStoragePaths.GetPhotosDirectory(), "exposure_missing1.png");

        Assert.Equal(expected, PhotoAssetStoragePaths.TryResolveReadPath("exposure_missing1"));
    }

    [Fact]
    public void ScopedFileTakesPrecedenceOverALegacyFileWithTheSameId()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-e");
        string scopedDir = PhotoAssetStoragePaths.GetPhotosDirectory();
        Directory.CreateDirectory(scopedDir);
        Directory.CreateDirectory(LegacyPhotosDirectory);

        string scopedFile = Path.Combine(scopedDir, "exposure_dup00001.png");
        string legacyFile = Path.Combine(LegacyPhotosDirectory, "exposure_dup00001.png");
        File.WriteAllBytes(scopedFile, new byte[] { 9 });
        File.WriteAllBytes(legacyFile, new byte[] { 1 });

        Assert.Equal(scopedFile, PhotoAssetStoragePaths.TryResolveReadPath("exposure_dup00001"));
    }

    // ---- ResolveReadPathForUse migrates a legacy hit into the scoped folder ----

    [Fact]
    public void ResolveForUseMovesALegacyHitIntoTheScopedFolder()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-g");
        Directory.CreateDirectory(LegacyPhotosDirectory);
        string legacyFile = Path.Combine(LegacyPhotosDirectory, "exposure_migrate1.png");
        File.WriteAllBytes(legacyFile, new byte[] { 7 });

        string scopedFile = Path.Combine(PhotoAssetStoragePaths.GetPhotosDirectory(), "exposure_migrate1.png");
        string resolved = PhotoAssetStoragePaths.ResolveReadPathForUse("exposure_migrate1");

        Assert.Equal(scopedFile, resolved);
        Assert.True(File.Exists(scopedFile));
        Assert.False(File.Exists(legacyFile));
        // After migration the photo is enumerable by the audit tooling, which the flat root was not.
        Assert.Contains("exposure_migrate1.png", PhotoAssetStoragePaths.EnumeratePhotoIds());
    }

    [Fact]
    public void ResolveForUseResetsTheMtimeSoAMigratedPhotoGetsAFreshGraceWindow()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-g2");
        Directory.CreateDirectory(LegacyPhotosDirectory);
        string legacyFile = Path.Combine(LegacyPhotosDirectory, "exposure_oldmtime.png");
        File.WriteAllBytes(legacyFile, new byte[] { 7 });
        File.SetLastWriteTimeUtc(legacyFile, new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        DateTime before = DateTime.UtcNow.AddSeconds(-5);
        PhotoAssetStoragePaths.ResolveReadPathForUse("exposure_oldmtime");

        // The audit reads mtime as its grace fallback for never-seen files; a migrated photo must not
        // inherit its ancient legacy write time (see ResolveReadPathForUse).
        DateTime? mtime = PhotoAssetStoragePaths.GetPhotoModifiedUtc("exposure_oldmtime");
        Assert.NotNull(mtime);
        Assert.True(mtime!.Value >= before, $"expected a fresh mtime, got {mtime:o}");
    }

    [Fact]
    public void ResolveForUseLeavesAnAlreadyScopedFileInPlace()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-h");
        string scopedDir = PhotoAssetStoragePaths.GetPhotosDirectory();
        Directory.CreateDirectory(scopedDir);
        string scopedFile = Path.Combine(scopedDir, "exposure_native01.png");
        File.WriteAllBytes(scopedFile, new byte[] { 3 });

        Assert.Equal(scopedFile, PhotoAssetStoragePaths.ResolveReadPathForUse("exposure_native01"));
        Assert.True(File.Exists(scopedFile));
    }

    [Fact]
    public void ResolveForUseDoesNotMoveWhenUnscoped()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(null);
        Directory.CreateDirectory(LegacyPhotosDirectory);
        string legacyFile = Path.Combine(LegacyPhotosDirectory, "exposure_flat0001.png");
        File.WriteAllBytes(legacyFile, new byte[] { 5 });

        Assert.Equal(legacyFile, PhotoAssetStoragePaths.ResolveReadPathForUse("exposure_flat0001"));
        Assert.True(File.Exists(legacyFile));
    }

    // ---- EnumeratePhotoIds only sees the scoped folder ----

    [Fact]
    public void EnumerateOnlySeesTheCurrentWorldsScopedFolder()
    {
        PhotoAssetStoragePaths.SetWorldScopeIdProvider(() => "world-f");
        string scopedDir = PhotoAssetStoragePaths.GetPhotosDirectory();
        Directory.CreateDirectory(scopedDir);
        Directory.CreateDirectory(LegacyPhotosDirectory);

        File.WriteAllBytes(Path.Combine(scopedDir, "exposure_scoped01.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(LegacyPhotosDirectory, "exposure_legacy02.png"), new byte[] { 1 });

        var ids = PhotoAssetStoragePaths.EnumeratePhotoIds();
        Assert.Contains("exposure_scoped01.png", ids);
        Assert.DoesNotContain("exposure_legacy02.png", ids);
    }
}
