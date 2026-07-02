using Xunit;
using Photocore.PhotoSync;

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
    [InlineData("  abc123.png  ", "abc123.png")]
    public void PassesThroughValidId(string input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    [Theory]
    [InlineData("abc123", "abc123.png")]
    [InlineData("abc123.PNG", "abc123.png")]
    public void AppendsPngExtensionIfMissing(string input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    // Lowercase is the canonical form: the seen index compares OrdinalIgnoreCase, so ids differing
    // only in case must land on one file on case-sensitive (Linux) filesystems too.
    [Theory]
    [InlineData("ABC123.PNG", "abc123.png")]
    [InlineData("Exposure_G8X4M2KD", "exposure_g8x4m2kd.png")]
    public void LowercasesToCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    // "__" is the derived-file separator (derived/<base>__*.png); an id containing it could
    // cross-match another photo's derived masks in the delete/prune globs.
    [Theory]
    [InlineData("abc__def")]
    [InlineData("exposure__g8x4m2kd.png")]
    public void RejectsDerivedFileSeparator(string input)
    {
        Assert.Equal(string.Empty, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    // Pin: legacy timestamped ids (pre-2.3 worlds, stamped into itemstack/block-entity attributes
    // that can never be batch-migrated) must resolve unchanged forever — with or without the .png
    // extension, which older seal code included in the attribute value.
    [Theory]
    [InlineData("exposure_2026-06-23_15-58-58_d9372891", "exposure_2026-06-23_15-58-58_d9372891.png")]
    [InlineData("exposure_2026-06-23_15-58-58_d9372891.png", "exposure_2026-06-23_15-58-58_d9372891.png")]
    public void LegacyIdsResolveUnchangedForever(string input, string expected)
    {
        Assert.Equal(expected, PhotoAssetStoragePaths.NormalizePhotoId(input));
    }

    [Fact]
    public void MintedIdHasStableShapeAndRoundTrips()
    {
        for (int i = 0; i < 200; i++)
        {
            string id = PhotoAssetStoragePaths.MintExposurePhotoIdCandidate();

            Assert.Matches("^exposure_[0-9abcdefghjkmnpqrstvwxyz]{8}$", id);
            Assert.DoesNotContain("__", id);

            // Canonical id is extensionless; the resolver maps it to the on-disk filename.
            Assert.Equal(id + ".png", PhotoAssetStoragePaths.NormalizePhotoId(id));
        }
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
