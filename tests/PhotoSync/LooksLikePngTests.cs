using Xunit;
using Photocore.PhotoSync.Runtime;

namespace Photocore.Tests.PhotoSync;

public class LooksLikePngTests
{
    private static readonly byte[] ValidPngHeader = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    };

    [Fact]
    public void AcceptsValidPngSignature()
    {
        byte[] buffer = new byte[16];
        Buffer.BlockCopy(ValidPngHeader, 0, buffer, 0, 8);

        Assert.True(PhotoAssetSyncCore.LooksLikePng(buffer, buffer.Length));
    }

    [Fact]
    public void RejectsTruncatedBuffer()
    {
        byte[] buffer = new byte[4];
        Buffer.BlockCopy(ValidPngHeader, 0, buffer, 0, 4);

        Assert.False(PhotoAssetSyncCore.LooksLikePng(buffer, buffer.Length));
    }

    [Fact]
    public void RejectsTotalSizeSmallerThanEight()
    {
        // Buffer is large enough but totalSize says it's only 4 bytes.
        byte[] buffer = new byte[16];
        Buffer.BlockCopy(ValidPngHeader, 0, buffer, 0, 8);

        Assert.False(PhotoAssetSyncCore.LooksLikePng(buffer, totalSize: 4));
    }

    [Fact]
    public void RejectsAllZeros()
    {
        byte[] buffer = new byte[16];
        Assert.False(PhotoAssetSyncCore.LooksLikePng(buffer, buffer.Length));
    }

    [Fact]
    public void RejectsJpegSignature()
    {
        // JPEG starts with FF D8 FF.
        byte[] buffer = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        Assert.False(PhotoAssetSyncCore.LooksLikePng(buffer, buffer.Length));
    }

    [Fact]
    public void RejectsZipSignature()
    {
        // ZIP starts with PK (0x50 0x4B).
        byte[] buffer = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00 };
        Assert.False(PhotoAssetSyncCore.LooksLikePng(buffer, buffer.Length));
    }

    [Fact]
    public void RejectsEmptyBuffer()
    {
        byte[] buffer = Array.Empty<byte>();
        Assert.False(PhotoAssetSyncCore.LooksLikePng(buffer, 0));
    }
}
