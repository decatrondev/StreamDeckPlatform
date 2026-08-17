using System.Text.Json;
using Deck.Plugins.Spotify.Tests.Fakes;
using Deck.SDK.Credentials;
using Deck.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deck.Plugins.Spotify.Tests;

public class SpotifyPluginTests : IAsyncLifetime
{
    private FakeSpotifyServer _server = null!;
    private InMemoryCredentialStore _credentials = null!;
    private SpotifyPlugin _plugin = null!;

    public async Task InitializeAsync()
    {
        _server = new FakeSpotifyServer();
        await _server.StartAsync();

        _credentials = new InMemoryCredentialStore();
        _plugin = NewPlugin();
        await _plugin.InitializeAsync(new TestPluginContext(_credentials));
    }

    public async Task DisposeAsync()
    {
        await _plugin.DisposeAsync();
        await _server.DisposeAsync();
    }

    private SpotifyPlugin NewPlugin() =>
        new("test-client-id", new HttpClient(), _server.BaseUrl.ToString(), _server.BaseUrl.ToString());

    [Fact]
    public async Task ConnectAsync_WithoutPriorAuthorization_ReportsNotAuthorized_DoesNotThrow()
    {
        await _plugin.ConnectAsync();

        Assert.Equal(SpotifyConnectionState.NotAuthorized, _plugin.ConnectionState);
    }

    [Fact]
    public async Task FullAuthorizationFlow_ValidatesPkce_StoresRefreshTokenViaCredentialManager()
    {
        var authUrl = _plugin.BeginAuthorization("http://127.0.0.1/callback");

        // El challenge sale de la URL que la UI abriría en el navegador — el
        // servidor falso lo usa para validar el PKCE de verdad, no de mentira.
        var challenge = ExtractQueryParam(authUrl, "code_challenge");
        _server.ExpectedCodeChallenge = challenge;

        await _plugin.CompleteAuthorizationAsync(_server.ExpectedAuthCode);

        Assert.Equal(SpotifyConnectionState.Connected, _plugin.ConnectionState);

        // Fase 4 valida justamente esto: el mismo Credential Manager de OBS
        // (Fase 3) sirve para guardar un refresh_token de OAuth.
        var stored = await _credentials.GetAsync("refresh-token");
        Assert.Equal(_server.ValidRefreshToken, stored);
    }

    [Fact]
    public async Task FullAuthorizationFlow_WithWrongChallenge_Fails()
    {
        var authUrl = _plugin.BeginAuthorization("http://127.0.0.1/callback");
        _server.ExpectedCodeChallenge = "challenge-que-no-corresponde";

        await Assert.ThrowsAsync<SpotifyAuthException>(
            () => _plugin.CompleteAuthorizationAsync(_server.ExpectedAuthCode));
    }

    [Fact]
    public async Task ConnectAsync_WithStoredRefreshToken_Reconnects()
    {
        await _credentials.SetAsync("refresh-token", _server.ValidRefreshToken);

        await _plugin.ConnectAsync();

        Assert.Equal(SpotifyConnectionState.Connected, _plugin.ConnectionState);
    }

    [Fact]
    public async Task ConnectAsync_WithRevokedRefreshToken_ReportsAuthenticationFailed_DoesNotThrow()
    {
        await _credentials.SetAsync("refresh-token", "un-token-que-ya-no-sirve");
        _server.RejectRefresh = true;

        await _plugin.ConnectAsync();

        Assert.Equal(SpotifyConnectionState.AuthenticationFailed, _plugin.ConnectionState);
    }

    [Fact]
    public async Task ExecuteActionAsync_Play_SendsRequestToPlayer()
    {
        await AuthorizeAsync();

        var result = await _plugin.ExecuteActionAsync("play", "{}");

        Assert.True(result.Success);
        Assert.Contains(_server.ReceivedApiCalls, c => c.Path == "/v1/me/player/play");
    }

    [Fact]
    public async Task ExecuteActionAsync_SetVolume_SendsVolumeParam()
    {
        await AuthorizeAsync();

        var result = await _plugin.ExecuteActionAsync("set-volume", """{"volume":42}""");

        Assert.True(result.Success);
        var call = Assert.Single(_server.ReceivedApiCalls, c => c.Path == "/v1/me/player/volume");
        Assert.Contains("volume_percent=42", call.Query);
    }

    [Fact]
    public async Task ExecuteActionAsync_AllPlaybackActions_Succeed()
    {
        await AuthorizeAsync();

        foreach (var action in new[] { "play", "pause", "next", "previous" })
        {
            var result = await _plugin.ExecuteActionAsync(action, "{}");
            Assert.True(result.Success, $"'{action}' debería haber tenido éxito: {result.Message}");
        }
    }

    [Fact]
    public async Task ExecuteActionAsync_WithoutAuthorizing_FailsGracefully_DoesNotThrow()
    {
        var result = await _plugin.ExecuteActionAsync("play", "{}");

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_AccessTokenExpiredMidSession_RefreshesAutomatically_ActionStillSucceeds()
    {
        await AuthorizeAsync();

        // Simula que el access_token venció justo antes de esta acción — el
        // plugin tiene que refrescar solo y reintentar, sin que se note.
        _server.RejectNextApiCall = true;

        var result = await _plugin.ExecuteActionAsync("play", "{}");

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task PluginEvent_TrackChanged_IsRaisedWhenTrackDiffers()
    {
        _server.CurrentlyPlayingTrackId = "track-A";
        await AuthorizeAsync(); // arranca el polling

        PluginEvent? received = null;
        _plugin.EventRaised += (_, e) => { if (e.EventId == "track-changed") received ??= e; };

        // Cambia lo que "está sonando" en el servidor falso — el próximo tick
        // de polling (cada 5s en producción; acá el test espera lo que haga
        // falta) tiene que notar la diferencia.
        await Task.Delay(200);
        _server.CurrentlyPlayingTrackId = "track-B";
        _server.CurrentlyPlayingTrackName = "Otra canción";

        var ok = await PollUntilAsync(() => received is not null, timeoutMs: 12000);

        Assert.True(ok, "No se recibió el evento track-changed a tiempo.");
        using var doc = JsonDocument.Parse(received!.PayloadJson);
        Assert.Equal("track-B", doc.RootElement.GetProperty("TrackId").GetString());
    }

    private async Task AuthorizeAsync()
    {
        var authUrl = _plugin.BeginAuthorization("http://127.0.0.1/callback");
        _server.ExpectedCodeChallenge = ExtractQueryParam(authUrl, "code_challenge");
        await _plugin.CompleteAuthorizationAsync(_server.ExpectedAuthCode);
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
            await Task.Delay(200);
            elapsed += 200;
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
