using Xunit;
using Photocore.PhotoSync.Storage;

namespace Photocore.Tests.PhotoSync;

public class NormalizePhotoIdTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(".", "")]
    [InlineData("..", "")]
    public void ReturnsEmptyForInvalidInput(string? input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input!));
    }

    [Theory]
    [InlineData("../secret.png", "")]
    [InlineData("../../etc/passwd", "")]
    [InlineData("foo/bar.png", "")]
    [InlineData("foo\\bar.png", "")]
    public void RejectsPathTraversal(string input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    [Fact]
    public void RejectsInvalidFileNameChars()
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            string input = $"photo{c}id.png";
            Assert.Equal(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId(input));
        }
    }

    [Theory]
    [InlineData("abc123.png", "abc123.png")]
    [InlineData("ABC123.PNG", "ABC123.PNG")]
    [InlineData("  abc123.png  ", "abc123.png")]
    public void PassesThroughValidId(string input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    [Theory]
    [InlineData("abc123", "abc123.png")]
    [InlineData("abc123.PNG", "abc123.PNG")]
    public void AppendsPngExtensionIfMissing(string input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    [Fact]
    public void RejectsOversizedId()
    {
        // Without extension, .png (4 chars) is appended — so 252+ chars would produce a 256+ char filename.
        string tooLong = new string('a', 252);
        Assert.Equal(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId(tooLong));

        // 251 chars + ".png" = exactly 255 — the filesystem boundary.
        string atLimit = new string('a', 251);
        Assert.NotEqual(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId(atLimit));

        // With an explicit .png extension, up to 255 chars is valid (no extension added).
        string atLimitWithExt = new string('a', 251) + ".png";
        Assert.NotEqual(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId(atLimitWithExt));

        string tooLongWithExt = new string('a', 252) + ".png";
        Assert.Equal(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId(tooLongWithExt));
    }

    [Fact]
    public void RejectsNullByteInjection()
    {
        Assert.Equal(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId("photo\0malicious.png"));
        Assert.Equal(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId("\0"));
    }
}
