using Deck.Plugins.Decatron.Tests.Fakes;
using Deck.SDK.Credentials;
using Deck.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deck.Plugins.Decatron.Tests;

public class DecatronPluginTests : IAsyncLifetime
{
    private FakeDecatronApiServer _server = null!;
    private DecatronPlugin _plugin = null!;
    private InMemoryCredentialStore _credentials = null!;

    public async Task InitializeAsync()
    {
        _server = new FakeDecatronApiServer();
        await _server.StartAsync();

        _plugin = new DecatronPlugin(new HttpClient(), _server.BaseUrl);
        _credentials = new InMemoryCredentialStore();
        await _plugin.InitializeAsync(new TestPluginContext(_credentials));
    }

    public async Task DisposeAsync()
    {
        await _plugin.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteActionAsync_SendChatMessage_UsesStoredDecatronToken_NoSeparateLoginNeeded()
    {
        await _credentials.SetAsync("access-token", _server.ExpectedToken);

        var result = await _plugin.ExecuteActionAsync("send-chat-message", """{"message":"hola desde flowdeck"}""");

        Assert.True(result.Success, result.Message);
        Assert.Equal("hola desde flowdeck", _server.LastReceivedMessage);
    }

    [Fact]
    public async Task ExecuteActionAsync_WithoutDecatronConnected_FailsGracefully_DoesNotThrow()
    {
        var result = await _plugin.ExecuteActionAsync("send-chat-message", """{"message":"hola"}""");

        Assert.False(result.Success);
        Assert.Contains("no conectada", result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_ExpiredOrRevokedToken_ReturnsFailure_DoesNotThrow()
    {
        await _credentials.SetAsync("access-token", "un-token-que-ya-no-sirve");

        var result = await _plugin.ExecuteActionAsync("send-chat-message", """{"message":"hola"}""");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteActionAsync_UnknownAction_FailsGracefully()
    {
        await _credentials.SetAsync("access-token", _server.ExpectedToken);

        var result = await _plugin.ExecuteActionAsync("nonexistent-action", "{}");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteActionAsync_SetTitle_UsesStoredDecatronToken()
    {
        await _credentials.SetAsync("access-token", _server.ExpectedToken);

        var result = await _plugin.ExecuteActionAsync("set-title", """{"title":"probando desde flowdeck"}""");

        Assert.True(result.Success, result.Message);
        Assert.Equal("probando desde flowdeck", _server.LastReceivedTitle);
    }

    [Fact]
    public async Task ExecuteActionAsync_SetCategory_SendsChosenGameId()
    {
        await _credentials.SetAsync("access-token", _server.ExpectedToken);

        var result = await _plugin.ExecuteActionAsync("set-category", """{"gameId":"509658"}""");

        Assert.True(result.Success, result.Message);
        Assert.Equal("509658", _server.LastReceivedGameId);
    }

    [Fact]
    public async Task SearchParameterOptionsAsync_SetCategory_ReturnsMatchingGames()
    {
        await _credentials.SetAsync("access-token", _server.ExpectedToken);
        _server.GameSearchResults = [("509658", "Just Chatting"), ("21779", "League of Legends")];

        var options = await _plugin.SearchParameterOptionsAsync("set-category", "gameId", "l");

        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.Value == "509658" && o.Label == "Just Chatting");
    }

    [Fact]
    public async Task SearchParameterOptionsAsync_WithoutDecatronConnected_ReturnsEmpty()
    {
        var options = await _plugin.SearchParameterOptionsAsync("set-category", "gameId", "l");

        Assert.Empty(options);
    }

    private sealed class TestPluginContext(ICredentialStore credentials) : IPluginContext
    {
        public ICredentialStore Credentials { get; } = credentials;
        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
