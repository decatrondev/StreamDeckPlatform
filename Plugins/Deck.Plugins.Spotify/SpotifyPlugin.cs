using System.Text.Json;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Plugins.Spotify;

// Segundo plugin (Fase 4) — primera integración con OAuth real. El contrato
// de Deck.SDK no modela "autorizar con el navegador" (eso es un paso previo,
// fuera del ciclo de vida InitializeAsync/ConnectAsync), así que
// BeginAuthorization/CompleteAuthorizationAsync son específicos de este
// plugin, no de IPlugin — quien construya la UI de "conectar Spotify" los
// llama directamente.
public sealed class SpotifyPlugin : IPlugin
{
    public const string PluginId = "spotify";

    private const string DefaultScopes =
        "user-modify-playback-state user-read-playback-state user-read-currently-playing";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly string _clientId;
    private readonly SpotifyOAuthClient _oauth;
    private readonly SpotifyApiClient _api;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    private IPluginContext? _context;
    private SpotifyTokens? _tokens;
    private string? _pendingCodeVerifier;
    private string? _pendingRedirectUri;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private string? _lastTrackId;

    public SpotifyConnectionState ConnectionState { get; private set; } = SpotifyConnectionState.NotAuthorized;

    // TODO(Fase 4→distribución): reemplazar por el Client ID real una vez
    // registrada la app "Flowdeck" en el dashboard de Spotify for Developers.
    // No es secreto (PKCE no lo necesita), así que puede ir hardcodeado.
    public SpotifyPlugin() : this("SPOTIFY_CLIENT_ID_NOT_CONFIGURED")
    {
    }

    public SpotifyPlugin(
        string clientId,
        HttpClient? httpClient = null,
        string authBaseUrl = "https://accounts.spotify.com",
        string apiBaseUrl = "https://api.spotify.com")
    {
        _clientId = clientId;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _oauth = new SpotifyOAuthClient(_http, authBaseUrl);
        _api = new SpotifyApiClient(_http, apiBaseUrl);
    }

    public PluginMetadata Metadata { get; } = new(
        Id: PluginId,
        Name: "Spotify",
        Version: "1.0.0",
        Author: "Flowdeck",
        Permissions: ["network"]);

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("play", "Reproducir"),
        new("pause", "Pausar"),
        new("next", "Siguiente canción"),
        new("previous", "Canción anterior"),
        new("set-volume", "Cambiar volumen", "Parámetro: volume (0-100).")
    ];

    public event EventHandler<PluginEvent>? EventRaised;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _context = context;
        return Task.CompletedTask;
    }

    // Paso 1 del login: arma la URL que la UI tiene que abrir en el navegador.
    public string BeginAuthorization(string redirectUri)
    {
        _pendingCodeVerifier = SpotifyOAuthClient.GenerateCodeVerifier();
        _pendingRedirectUri = redirectUri;
        return _oauth.BuildAuthorizationUrl(_clientId, redirectUri, DefaultScopes, _pendingCodeVerifier);
    }

    // Paso 2: la UI capturó el "code" del redirect (típicamente con un
    // listener HTTP local en loopback) y lo entrega acá. Guarda el
    // refresh_token vía el Credential Manager del Core — la validación real
    // de que ese mecanismo sirve para OAuth, no solo para una contraseña.
    public async Task CompleteAuthorizationAsync(string authorizationCode, CancellationToken ct = default)
    {
        if (_pendingCodeVerifier is null || _pendingRedirectUri is null)
        {
            throw new InvalidOperationException("Llamá BeginAuthorization antes de CompleteAuthorizationAsync.");
        }

        _tokens = await _oauth.ExchangeCodeAsync(_clientId, authorizationCode, _pendingCodeVerifier, _pendingRedirectUri, ct);
        await SaveRefreshTokenAsync(ct);

        _pendingCodeVerifier = null;
        _pendingRedirectUri = null;

        ConnectionState = SpotifyConnectionState.Connected;
        StartPolling();
        RaiseConnectionStateEvent();
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var storedRefreshToken = _context is null ? null : await _context.Credentials.GetAsync("refresh-token", ct);

        if (storedRefreshToken is null)
        {
            ConnectionState = SpotifyConnectionState.NotAuthorized;
            RaiseConnectionStateEvent();
            return;
        }

        try
        {
            _tokens = await _oauth.RefreshAsync(_clientId, storedRefreshToken, ct);
            await SaveRefreshTokenAsync(ct);
            ConnectionState = SpotifyConnectionState.Connected;
            StartPolling();
        }
        catch (SpotifyAuthException)
        {
            // Refresh token revocado o inválido — no crashea, solo reporta el
            // estado. El usuario necesita reautorizar (BeginAuthorization de
            // nuevo), el Core no lo reintenta solo.
            ConnectionState = SpotifyConnectionState.AuthenticationFailed;
        }

        RaiseConnectionStateEvent();
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        StopPolling();
        ConnectionState = SpotifyConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        if (_tokens is null)
        {
            return PluginActionResult.Fail("Spotify no está conectado — falta autorizar la cuenta.");
        }

        try
        {
            return await ExecuteWithRetryAsync(actionId, parametersJson, ct);
        }
        catch (SpotifyApiException ex)
        {
            return PluginActionResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Error inesperado hablando con Spotify: {ex.Message}");
        }
    }

    private async Task<PluginActionResult> ExecuteWithRetryAsync(string actionId, string parametersJson, CancellationToken ct)
    {
        try
        {
            return await RunActionAsync(actionId, parametersJson, ct);
        }
        catch (SpotifyUnauthorizedException)
        {
            // El access_token venció a mitad de sesión — se refresca solo y
            // se reintenta una vez, sin que el usuario lo note (checklist:
            // "maneja credenciales expiradas/inválidas sin crashear").
            if (!await TryRefreshAsync(ct))
            {
                return PluginActionResult.Fail("La sesión de Spotify venció y no se pudo renovar — hay que volver a autorizar.");
            }

            return await RunActionAsync(actionId, parametersJson, ct);
        }
    }

    private async Task<PluginActionResult> RunActionAsync(string actionId, string parametersJson, CancellationToken ct)
    {
        if (_tokens!.IsExpired && !await TryRefreshAsync(ct))
        {
            return PluginActionResult.Fail("La sesión de Spotify venció y no se pudo renovar — hay que volver a autorizar.");
        }

        var token = _tokens!.AccessToken;

        switch (actionId)
        {
            case "play":
                await _api.PlayAsync(token, ct);
                return PluginActionResult.Ok("Reproduciendo.");
            case "pause":
                await _api.PauseAsync(token, ct);
                return PluginActionResult.Ok("Pausado.");
            case "next":
                await _api.NextAsync(token, ct);
                return PluginActionResult.Ok("Siguiente canción.");
            case "previous":
                await _api.PreviousAsync(token, ct);
                return PluginActionResult.Ok("Canción anterior.");
            case "set-volume":
                var volume = JsonDocument.Parse(parametersJson).RootElement.GetProperty("volume").GetInt32();
                await _api.SetVolumeAsync(token, volume, ct);
                return PluginActionResult.Ok($"Volumen: {volume}%.");
            default:
                return PluginActionResult.Fail($"Acción desconocida: '{actionId}'.");
        }
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        try
        {
            _tokens = await _oauth.RefreshAsync(_clientId, _tokens!.RefreshToken, ct);
            await SaveRefreshTokenAsync(ct);
            return true;
        }
        catch (SpotifyAuthException)
        {
            ConnectionState = SpotifyConnectionState.AuthenticationFailed;
            RaiseConnectionStateEvent();
            return false;
        }
    }

    private Task SaveRefreshTokenAsync(CancellationToken ct) =>
        _context?.Credentials.SetAsync("refresh-token", _tokens!.RefreshToken, ct) ?? Task.CompletedTask;

    // Spotify no tiene webhooks de "canción cambió" — se hace polling liviano
    // mientras esté conectado, y solo se emite el evento cuando el track
    // efectivamente cambia (no en cada tick).
    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        _pollTask = null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_tokens is not null)
                {
                    var current = await _api.GetCurrentlyPlayingAsync(_tokens.AccessToken, ct);
                    if (current?.TrackId != _lastTrackId)
                    {
                        _lastTrackId = current?.TrackId;
                        EventRaised?.Invoke(this, new PluginEvent(
                            "track-changed",
                            JsonSerializer.Serialize(current),
                            DateTimeOffset.UtcNow));
                    }
                }
            }
            catch
            {
                // Un tick de polling que falla no debe tumbar el loop — se
                // reintenta en el próximo ciclo.
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void RaiseConnectionStateEvent() =>
        EventRaised?.Invoke(this, new PluginEvent(
            "connection-state",
            JsonSerializer.Serialize(new { state = ConnectionState.ToString() }),
            DateTimeOffset.UtcNow));

    public ValueTask DisposeAsync()
    {
        StopPolling();
        if (_ownsHttpClient) _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
