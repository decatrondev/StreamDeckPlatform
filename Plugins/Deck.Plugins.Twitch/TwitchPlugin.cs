using System.Linq;
using System.Text.Json;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Plugins.Twitch;

// Cuarto y último plugin del MVP (Fase 6) — el más exigente en tiempo real:
// además del ciclo de vida normal, ConnectAsync tiene que levantar el
// WebSocket de EventSub, esperar el session_welcome, y recién ahí dar de alta
// las suscripciones por REST usando ese session_id.
public sealed class TwitchPlugin : IPlugin
{
    public const string PluginId = "twitch";

    // "channel:manage:moderation" no existe como scope de Twitch (typo) y
    // además no lo usa ninguna acción de este plugin (no hay ban/timeout) —
    // Twitch rechazaba el login entero con "invalid scope requested" antes
    // de sacarlo.
    private const string DefaultScopes =
        "channel:manage:broadcast user:write:chat moderator:read:followers channel:read:subscriptions";

    private static readonly (string Type, string Version)[] EventSubscriptions =
    [
        ("channel.follow", "2"),
        ("channel.subscribe", "1"),
        ("channel.raid", "1")
    ];

    private readonly string _clientId;
    private readonly TwitchOAuthClient _oauth;
    private readonly TwitchApiClient _api;
    private readonly TwitchEventSubClient _eventSub;

    private IPluginContext? _context;
    private TwitchTokens? _tokens;
    private string? _broadcasterId;
    private string? _pendingCodeVerifier;
    private string? _pendingRedirectUri;

    public TwitchConnectionState ConnectionState => _eventSub.State;

    public TwitchPlugin() : this("TWITCH_CLIENT_ID_NOT_CONFIGURED")
    {
    }

    public TwitchPlugin(
        string clientId,
        HttpClient? httpClient = null,
        string authBaseUrl = "https://id.twitch.tv",
        string apiBaseUrl = "https://api.twitch.tv",
        Uri? eventSubUri = null,
        string tokenProxyUrl = "https://twitch.decatron.net/api/oauth/twitch/token")
    {
        _clientId = clientId;
        var http = httpClient ?? new HttpClient();
        _oauth = new TwitchOAuthClient(http, authBaseUrl, tokenProxyUrl);
        _api = new TwitchApiClient(http, clientId, apiBaseUrl);
        _eventSub = new TwitchEventSubClient(eventSubUri);
        _eventSub.StateChanged += OnConnectionStateChanged;
        _eventSub.NotificationReceived += OnNotification;
    }

    public PluginMetadata Metadata { get; } = new(
        Id: PluginId,
        Name: "Twitch",
        Version: "1.0.0",
        Author: "Flowdeck",
        Permissions: ["network"]);

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("set-title", "Cambiar título del stream", "Parámetro: title.",
            """{"fields":[{"key":"title","label":"Título","type":"text","required":true}]}"""),
        new("set-category", "Cambiar categoría", "Buscá el juego/categoría por nombre.",
            """{"fields":[{"key":"categoryId","label":"Categoría","type":"search","required":true}]}"""),
        new("create-marker", "Crear marcador", "Parámetro opcional: description.",
            """{"fields":[{"key":"description","label":"Descripción (opcional)","type":"text","required":false}]}"""),
        new("send-chat-message", "Enviar mensaje al chat", "Parámetro: message.",
            """{"fields":[{"key":"message","label":"Mensaje","type":"text","required":true}]}""")
    ];

    public event EventHandler<PluginEvent>? EventRaised;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public string BeginAuthorization(string redirectUri)
    {
        _pendingCodeVerifier = TwitchOAuthClient.GenerateCodeVerifier();
        _pendingRedirectUri = redirectUri;
        return _oauth.BuildAuthorizationUrl(_clientId, redirectUri, DefaultScopes, _pendingCodeVerifier);
    }

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

        await StartEventSubAsync(ct);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var storedRefreshToken = _context is null ? null : await _context.Credentials.GetAsync("refresh-token", ct);
        if (storedRefreshToken is null) return; // sin autorizar todavía — no hay nada que conectar

        try
        {
            _tokens = await _oauth.RefreshAsync(_clientId, storedRefreshToken, ct);
            await SaveRefreshTokenAsync(ct);
            await StartEventSubAsync(ct);
        }
        catch (TwitchAuthException)
        {
            // Refresh token revocado — no reintenta solo, necesita reautorización.
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default) => _eventSub.StopAsync();

    // No existía forma de "olvidar" la cuenta conectada (DisconnectAsync solo
    // corta el WebSocket de EventSub, no borra el refresh-token) — hacía
    // falta para poder desconectar Twitch desde Ajustes, sobre todo ahora que
    // es mutuamente excluyente con Decatron.
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _eventSub.StopAsync();
        _tokens = null;
        _broadcasterId = null;

        if (_context is not null)
        {
            await _context.Credentials.DeleteAsync("refresh-token", ct);
        }
    }

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        if (_tokens is null)
        {
            return PluginActionResult.Fail("Twitch no está conectado — falta autorizar la cuenta.");
        }

        try
        {
            var parameters = string.IsNullOrWhiteSpace(parametersJson) ? default : JsonDocument.Parse(parametersJson).RootElement;
            return await ExecuteWithRetryAsync(actionId, parameters, ct);
        }
        catch (TwitchApiException ex)
        {
            return PluginActionResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Error inesperado hablando con Twitch: {ex.Message}");
        }
    }

    private async Task<PluginActionResult> ExecuteWithRetryAsync(string actionId, JsonElement parameters, CancellationToken ct)
    {
        try
        {
            return await RunActionAsync(actionId, parameters, ct);
        }
        catch (TwitchUnauthorizedException)
        {
            if (!await TryRefreshAsync(ct))
            {
                return PluginActionResult.Fail("La sesión de Twitch venció y no se pudo renovar — hay que volver a autorizar.");
            }

            return await RunActionAsync(actionId, parameters, ct);
        }
    }

    private async Task<PluginActionResult> RunActionAsync(string actionId, JsonElement parameters, CancellationToken ct)
    {
        if (_tokens!.IsExpired && !await TryRefreshAsync(ct))
        {
            return PluginActionResult.Fail("La sesión de Twitch venció y no se pudo renovar — hay que volver a autorizar.");
        }

        var token = _tokens!.AccessToken;
        var broadcasterId = _broadcasterId ??= await _api.GetUserIdAsync(token, ct);

        switch (actionId)
        {
            case "set-title":
                await _api.ModifyChannelAsync(token, broadcasterId, parameters.GetProperty("title").GetString(), null, ct);
                return PluginActionResult.Ok("Título actualizado.");

            case "set-category":
                await _api.ModifyChannelAsync(token, broadcasterId, null, parameters.GetProperty("categoryId").GetString(), ct);
                return PluginActionResult.Ok("Categoría actualizada.");

            case "create-marker":
                var description = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("description", out var d)
                    ? d.GetString()
                    : null;
                await _api.CreateStreamMarkerAsync(token, broadcasterId, description, ct);
                return PluginActionResult.Ok("Marcador creado.");

            case "send-chat-message":
                var message = parameters.GetProperty("message").GetString()!;
                await _api.SendChatMessageAsync(token, broadcasterId, broadcasterId, message, ct);
                return PluginActionResult.Ok("Mensaje enviado al chat.");

            default:
                return PluginActionResult.Fail($"Acción desconocida: '{actionId}'.");
        }
    }

    public async Task<IReadOnlyList<ParameterOption>> SearchParameterOptionsAsync(
        string actionId, string parameterKey, string query, CancellationToken ct = default)
    {
        if (_tokens is null || (actionId, parameterKey) != ("set-category", "categoryId") || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var results = await _api.SearchCategoriesAsync(_tokens.AccessToken, query, ct);
            return results.Select(c => new ParameterOption(c.Id, c.Name)).ToList();
        }
        catch
        {
            // Sesión vencida, timeout, lo que sea — el autocomplete se queda
            // vacío para esa búsqueda, no rompe el diálogo.
            return [];
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
        catch (TwitchAuthException)
        {
            return false;
        }
    }

    private Task SaveRefreshTokenAsync(CancellationToken ct) =>
        _context?.Credentials.SetAsync("refresh-token", _tokens!.RefreshToken, ct) ?? Task.CompletedTask;

    private async Task StartEventSubAsync(CancellationToken ct)
    {
        _broadcasterId ??= await _api.GetUserIdAsync(_tokens!.AccessToken, ct);
        _eventSub.Start();

        // Espera al session_welcome antes de intentar suscribirse — sin
        // session_id, Twitch rechaza cualquier alta de suscripción.
        for (var i = 0; i < 50 && _eventSub.SessionId is null; i++)
        {
            await Task.Delay(100, ct);
        }

        if (_eventSub.SessionId is null) return; // no llegó a tiempo, el propio loop sigue reintentando

        foreach (var (type, version) in EventSubscriptions)
        {
            try
            {
                await _api.CreateEventSubSubscriptionAsync(
                    _tokens!.AccessToken, type, version,
                    new { broadcaster_user_id = _broadcasterId, moderator_user_id = _broadcasterId },
                    _eventSub.SessionId, ct);
            }
            catch
            {
                // Una suscripción que falla (ej. falta un scope puntual) no
                // debe tirar abajo la conexión ni las demás suscripciones.
            }
        }
    }

    private void OnNotification(string subscriptionType, JsonElement eventData)
    {
        var mapped = subscriptionType switch
        {
            "channel.follow" => "follow",
            "channel.subscribe" => "subscribe",
            "channel.raid" => "raid",
            _ => subscriptionType
        };

        EventRaised?.Invoke(this, new PluginEvent(mapped, eventData.GetRawText(), DateTimeOffset.UtcNow));
    }

    private void OnConnectionStateChanged(TwitchConnectionState state) =>
        EventRaised?.Invoke(this, new PluginEvent(
            "connection-state",
            JsonSerializer.Serialize(new { state = state.ToString() }),
            DateTimeOffset.UtcNow));

    public async ValueTask DisposeAsync()
    {
        _eventSub.StateChanged -= OnConnectionStateChanged;
        _eventSub.NotificationReceived -= OnNotification;
        await _eventSub.DisposeAsync();
    }
}
