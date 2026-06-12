using Xunit;
using Collodion.PhotoMetadata.Model;

namespace Collodion.Tests.PhotoMetadata;

public class PhotoLastSeenIndexTests
{
    [Fact]
    public void TouchNewIdCreatesEntryWithBothTimestamps()
    {
        var index = new PhotoLastSeenIndex();
        index.Touch("abc123.png");

        Assert.True(index.Entries.TryGetValue("abc123.png", out var entry));
        Assert.NotEmpty(entry!.FirstSeenUtc);
        Assert.NotEmpty(entry.LastSeenUtc);
        Assert.Equal(entry.FirstSeenUtc, entry.LastSeenUtc);
    }

    [Fact]
    public void TouchExistingIdUpdatesLastSeenPreservesFirstSeen()
    {
        var index = new PhotoLastSeenIndex();
        index.Entries["abc123.png"] = new PhotoLastSeenEntry
        {
            FirstSeenUtc = "2020-01-01T00:00:00Z",
            LastSeenUtc = "2020-01-01T00:00:00Z"
        };

        index.Touch("abc123.png");

        Assert.Equal("2020-01-01T00:00:00Z", index.Entries["abc123.png"].FirstSeenUtc);
        Assert.NotEqual("2020-01-01T00:00:00Z", index.Entries["abc123.png"].LastSeenUtc);
    }

    [Fact]
    public void TouchHealsEmptyFirstSeenOnExistingEntry()
    {
        var index = new PhotoLastSeenIndex();
        index.Entries["abc123.png"] = new PhotoLastSeenEntry
        {
            FirstSeenUtc = string.Empty,
            LastSeenUtc = "2020-01-01T00:00:00Z"
        };

        index.Touch("abc123.png");

        Assert.NotEmpty(index.Entries["abc123.png"].FirstSeenUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape.png")]
    public void TouchInvalidIdDoesNothing(string? photoId)
    {
        var index = new PhotoLastSeenIndex();
        index.Touch(photoId!);

        Assert.Empty(index.Entries);
    }

    [Fact]
    public void ClampInPlaceRemovesWhitespaceKeys()
    {
        var index = new PhotoLastSeenIndex();
        index.Entries["valid.png"] = new PhotoLastSeenEntry { FirstSeenUtc = "x", LastSeenUtc = "x" };
        index.Entries["   "] = new PhotoLastSeenEntry();
        index.Entries[string.Empty] = new PhotoLastSeenEntry();

        index.ClampInPlace();

        Assert.True(index.Entries.ContainsKey("valid.png"));
        Assert.False(index.Entries.ContainsKey("   "));
        Assert.False(index.Entries.ContainsKey(string.Empty));
    }

    [Fact]
    public void TouchIsCaseInsensitive()
    {
        var index = new PhotoLastSeenIndex();
        index.Touch("ABC123.PNG");
        index.Touch("abc123.png");

        // OrdinalIgnoreCase comparer means both touches hit the same entry.
        Assert.Single(index.Entries);
    }
}
