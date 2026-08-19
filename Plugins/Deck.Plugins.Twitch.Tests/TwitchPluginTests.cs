using System.Text.Json;
using Deck.Plugins.Twitch.Tests.Fakes;
using Deck.SDK.Credentials;
using Deck.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deck.Plugins.Twitch.Tests;

public class TwitchPluginTests : IAsyncLifetime
{
    private FakeTwitchApiServer _apiServer = null!;
    private FakeTwitchEventSubServer _eventSubServer = null!;
    private InMemoryCredentialStore _credentials = null!;
    private TwitchPlugin _plugin = null!;

    public async Task InitializeAsync()
    {
        _apiServer = new FakeTwitchApiServer();
        await _apiServer.StartAsync();

        _eventSubServer = new FakeTwitchEventSubServer { KeepaliveTimeoutSeconds = 1 };
        await _eventSubServer.StartAsync();

        _credentials = new InMemoryCredentialStore();

        _plugin = new TwitchPlugin(
            _apiServer.ExpectedClientId, new HttpClient(),
            _apiServer.BaseUrl.ToString(), _apiServer.BaseUrl.ToString(),
            _eventSubServer.Uri,
            // El canje de token ya no va directo a authBaseUrl (ver comentario
            // en TwitchOAuthClient) — en test apunta al mismo fake server,
            // que sirve /oauth2/token sin exigir secret.
            $"{_apiServer.BaseUrl}oauth2/token");

        await _plugin.InitializeAsync(new TestPluginContext(_credentials));
    }

    public async Task DisposeAsync()
    {
        await _plugin.DisposeAsync();
        await _apiServer.DisposeAsync();
        await _eventSubServer.DisposeAsync();
    }

    [Fact]
    public async Task FullAuthorizationFlow_ValidatesPkce_StoresRefreshTokenViaCredentialManager()
    {
        var authUrl = _plugin.BeginAuthorization("http://127.0.0.1/callback");
        _apiServer.ExpectedCodeChallenge = ExtractQueryParam(authUrl, "code_challenge");

        await _plugin.CompleteAuthorizationAsync(_apiServer.ExpectedAuthCode);

        var stored = await _credentials.GetAsync("refresh-token");
        Assert.Equal(_apiServer.ValidRefreshToken, stored);
    }

    [Fact]
    public async Task FullAuthorizationFlow_ConnectsEventSubAndSubscribes()
    {
        await AuthorizeAsync();

        var ok = await PollUntilAsync(() => _plugin.ConnectionState == TwitchConnectionState.Connected, 5000);
        Assert.True(ok, $"No llegó a Connected (quedó en '{_plugin.ConnectionState}').");

        var subscriptionCalls = _apiServer.ReceivedApiCalls.Where(c => c.Path.StartsWith("/helix/eventsub/subscriptions")).ToList();
        Assert.Equal(3, subscriptionCalls.Count); // follow, subscribe, raid
    }

    [Fact]
    public async Task ExecuteActionAsync_SetTitle_SendsCorrectRequest()
    {
        await AuthorizeAsync();

        var result = await _plugin.ExecuteActionAsync("set-title", """{"title":"Jugando algo copado"}""");

        Assert.True(result.Success, result.Message);
        var call = Assert.Single(_apiServer.ReceivedApiCalls, c => c.Path.StartsWith("/helix/channels"));
        Assert.Contains("Jugando algo copado", call.Body);
    }

    [Fact]
    public async Task ExecuteActionAsync_CreateMarker_SendsCorrectRequest()
    {
        await AuthorizeAsync();

        var result = await _plugin.ExecuteActionAsync("create-marker", """{"description":"clip esto"}""");

        Assert.True(result.Success, result.Message);
        Assert.Contains(_apiServer.ReceivedApiCalls, c => c.Path.StartsWith("/helix/streams/markers"));
    }

    [Fact]
    public async Task ExecuteActionAsync_SendChatMessage_SendsCorrectRequest()
    {
        await AuthorizeAsync();

        var result = await _plugin.ExecuteActionAsync("send-chat-message", """{"message":"hola desde flowdeck"}""");

        Assert.True(result.Success, result.Message);
        var call = Assert.Single(_apiServer.ReceivedApiCalls, c => c.Path.StartsWith("/helix/chat/messages"));
        Assert.Contains("hola desde flowdeck", call.Body);
    }

    [Fact]
    public async Task SearchParameterOptionsAsync_SetCategory_ReturnsMatchingCategories()
    {
        await AuthorizeAsync();
        _apiServer.CategorySearchResults = [("509658", "Just Chatting"), ("21779", "League of Legends")];

        var options = await _plugin.SearchParameterOptionsAsync("set-category", "categoryId", "l");

        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.Value == "509658" && o.Label == "Just Chatting");
        Assert.Contains(options, o => o.Value == "21779" && o.Label == "League of Legends");
    }

    [Fact]
    public async Task SearchParameterOptionsAsync_WithoutAuthorizing_ReturnsEmpty_DoesNotThrow()
    {
        var options = await _plugin.SearchParameterOptionsAsync("set-category", "categoryId", "l");

        Assert.Empty(options);
    }

    [Fact]
    public async Task ExecuteActionAsync_WithoutAuthorizing_FailsGracefully_DoesNotThrow()
    {
        var result = await _plugin.ExecuteActionAsync("set-title", """{"title":"x"}""");

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_AccessTokenExpiredMidSession_RefreshesAutomatically_ActionStillSucceeds()
    {
        await AuthorizeAsync();
        _apiServer.RejectNextApiCall = true;

        var result = await _plugin.ExecuteActionAsync("set-title", """{"title":"nuevo título"}""");

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task PluginEvent_Follow_IsRaisedFromEventSubNotification()
    {
        await AuthorizeAsync();
        await PollUntilAsync(() => _plugin.ConnectionState == TwitchConnectionState.Connected, 5000);

        PluginEvent? received = null;
        _plugin.EventRaised += (_, e) => { if (e.EventId == "follow") received ??= e; };

        await _eventSubServer.SendNotificationAsync("channel.follow", new { user_name = "un_seguidor_nuevo" });

        var ok = await PollUntilAsync(() => received is not null, 3000);

        Assert.True(ok, "No se recibió el evento 'follow' a tiempo.");
        using var doc = JsonDocument.Parse(received!.PayloadJson);
        Assert.Equal("un_seguidor_nuevo", doc.RootElement.GetProperty("user_name").GetString());
    }

    [Fact]
    public async Task Disconnection_ThenTwitchComesBack_ReconnectsAutomatically_ViaKeepaliveTimeout()
    {
        await AuthorizeAsync();
        await PollUntilAsync(() => _plugin.ConnectionState == TwitchConnectionState.Connected, 5000);

        // Sin frame de cierre — simula una caída de red real. Solo el
        // watchdog de keepalive (armado con timeout de 1s en este test) tiene
        // que notar que la conexión está muerta.
        _eventSubServer.DropConnection();

        var reconnected = await PollUntilAsync(
            () => _plugin.ConnectionState == TwitchConnectionState.Connected, timeoutMs: 15000);

        Assert.True(reconnected, $"Debería haberse reconectado solo (quedó en '{_plugin.ConnectionState}').");
    }

    private async Task AuthorizeAsync()
    {
        var authUrl = _plugin.BeginAuthorization("http://127.0.0.1/callback");
        _apiServer.ExpectedCodeChallenge = ExtractQueryParam(authUrl, "code_challenge");
        await _plugin.CompleteAuthorizationAsync(_apiServer.ExpectedAuthCode);
    }

    private static string ExtractQueryParam(string url, string paramName)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == paramName) return Uri.UnescapeDataString(parts[1]);
        }
        throw new InvalidOperationException($"'{paramName}' no está en la URL: {url}");
    }

    private static async Task<bool> PollUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var elapsed = 0;
        while (elapsed < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(100);
            elapsed += 100;
        }
        return condition();
    }

    private sealed class TestPluginContext : IPluginContext
    {
        public TestPluginContext(ICredentialStore credentials) => Credentials = credentials;
        public ICredentialStore Credentials { get; }
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
