using Deck.Core.Icons;

namespace Deck.Core.Tests;

public class IconStoreTests : IDisposable
{
    private readonly string _appDataDir = Path.Combine(Path.GetTempPath(), $"deck-test-icons-{Guid.NewGuid()}");
    private readonly IconStore _store;

    public IconStoreTests()
    {
        _store = new IconStore(_appDataDir);
    }

    [Fact]
    public async Task SaveCustomIconAsync_CopiesFileAndReturnsFileRef()
    {
        using var source = new MemoryStream([1, 2, 3, 4]);

        var iconRef = await _store.SaveCustomIconAsync(source, ".png");

        Assert.StartsWith("file:", iconRef);
        Assert.True(File.Exists(_store.ResolveFilePath(iconRef)));
    }

    [Fact]
    public async Task ResolveFilePath_RoundTripsTheSavedBytes()
    {
        byte[] original = [9, 8, 7, 6, 5];
        using var source = new MemoryStream(original);

        var iconRef = await _store.SaveCustomIconAsync(source, ".jpg");
        var path = _store.ResolveFilePath(iconRef)!;

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public void ResolveFilePath_EmojiRef_ReturnsNull()
    {
        Assert.Null(_store.ResolveFilePath("emoji:🎤"));
    }

    [Fact]
    public void ResolveFilePath_NullRef_ReturnsNull()
    {
        Assert.Null(_store.ResolveFilePath(null));
    }

    [Fact]
    public void ResolveEmoji_EmojiRef_ReturnsGlyph()
    {
        Assert.Equal("🎤", IconStore.ResolveEmoji("emoji:🎤"));
    }

    [Fact]
    public void ResolveEmoji_FileRef_ReturnsNull()
    {
        Assert.Null(IconStore.ResolveEmoji("file:abc.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_appDataDir)) Directory.Delete(_appDataDir, recursive: true);
    }
}
