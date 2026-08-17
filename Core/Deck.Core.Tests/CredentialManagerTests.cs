using Deck.Core.Credentials;
using Deck.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Deck.Core.Tests;

public class CredentialManagerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"deck-test-{Guid.NewGuid()}.db");
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"deck-test-key-{Guid.NewGuid()}.txt");
    private readonly DeckDbContext _db;
    private readonly SqliteCredentialManager _manager;

    public CredentialManagerTests()
    {
        _db = new DeckDbContext(DeckDb.CreateOptions(_dbPath));
        DeckDb.EnsureMigrated(_db);
        _manager = new SqliteCredentialManager(_db, CredentialEncryptionKey.LoadOrCreate(_keyPath));
    }

    [Fact]
    public async Task SetAndGet_RoundTrips()
    {
        await _manager.SetAsync("twitch", "oauth-token", "super-secreto-123");

        var value = await _manager.GetAsync("twitch", "oauth-token");

        Assert.Equal("super-secreto-123", value);
    }

    [Fact]
    public async Task Get_UnknownKey_ReturnsNull()
    {
        var value = await _manager.GetAsync("twitch", "no-existe");
        Assert.Null(value);
    }

    [Fact]
    public async Task StoredValue_IsNeverPlaintext()
    {
        await _manager.SetAsync("spotify", "refresh-token", "esto-no-deberia-verse-asi");

        var row = await _db.Credentials.FirstAsync(c => c.PluginId == "spotify" && c.Key == "refresh-token");

        var cipherAsText = System.Text.Encoding.UTF8.GetString(row.CipherText);
        Assert.DoesNotContain("esto-no-deberia-verse-asi", cipherAsText);
    }

    [Fact]
    public async Task Set_TwoDifferentPlugins_SameKey_DoNotCollide()
    {
        await _manager.SetAsync("obs", "api-key", "valor-obs");
        await _manager.SetAsync("discord", "api-key", "valor-discord");

        Assert.Equal("valor-obs", await _manager.GetAsync("obs", "api-key"));
        Assert.Equal("valor-discord", await _manager.GetAsync("discord", "api-key"));
    }

    [Fact]
    public async Task Delete_RemovesCredential()
    {
        await _manager.SetAsync("obs", "password", "1234");
        await _manager.DeleteAsync("obs", "password");

        Assert.Null(await _manager.GetAsync("obs", "password"));
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_keyPath);
    }
}
