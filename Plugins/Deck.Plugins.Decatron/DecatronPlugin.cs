using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Plugins.Decatron;

// No es un login propio — usa el mismo token que ya guarda DecatronAuthService
// (Deck.Core.Auth) bajo el mismo Id de plugin ("decatron"), así que
// IPluginContext.Credentials (que el Core escopea por plugin id) lee
// exactamente el access-token que "Conectar con Decatron" ya dejó guardado.
// Sin esto, cada acción de Twitch pedía su propio login aparte — con este
// plugin, un solo login de Decatron alcanza para mandar mensajes al chat,
// cambiar categoría y cambiar título (todo reenviado por el bot con el token
// que ya tiene guardado de cada streamer — el mismo que usan !game/!title en
// el chat). Ver plan en
// Flowdeck.Web.Api/content/docs/02-integracion-bot-decatron/01-plan.md.
public sealed class DecatronPlugin : IPlugin
{
    public const string PluginId = "decatron";

    private const string DefaultApiBaseUrl = "https://twitch.decatron.net/api/v1";

    private readonly HttpClient _http;
    private readonly string _apiBaseUrl;

    private IPluginContext? _context;

    public DecatronPlugin() : this(new HttpClient(), DefaultApiBaseUrl)
    {
    }

    // Para tests: apuntar al servidor falso en vez de la API real.
    public DecatronPlugin(HttpClient http, string apiBaseUrl = DefaultApiBaseUrl)
    {
        _http = http;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
    }

    public PluginMetadata Metadata { get; } = new(
        Id: PluginId,
        Name: "Decatron",
        Version: "1.0.0",
        Author: "Flowdeck",
        Permissions: ["network"]);

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("send-chat-message", "Enviar mensaje al chat", "Usa tu cuenta de Decatron — no hace falta login aparte de Twitch.",
            """{"fields":[{"key":"message","label":"Mensaje","type":"text","required":true}]}"""),
        new("set-category", "Cambiar categoría", "Buscá el juego/categoría por nombre — mismo buscador que usa !game.",
            """{"fields":[{"key":"gameId","label":"Categoría","type":"search","required":true}]}"""),
        new("set-title", "Cambiar título del stream", "Parámetro: title.",
            """{"fields":[{"key":"title","label":"Título","type":"text","required":true}]}"""),
        new("timer-start", "Iniciar timer", "Duración opcional en segundos — vacío usa la última configurada.",
            """{"fields":[{"key":"duration","label":"Duración en segundos (opcional)","type":"number","required":false}]}"""),
        new("timer-pause", "Pausar timer"),
        new("timer-resume", "Reanudar timer"),
        new("timer-stop", "Detener timer"),
        new("timer-add", "Agregar tiempo al timer", "Segundos a sumar (puede ser negativo para restar).",
            """{"fields":[{"key":"seconds","label":"Segundos","type":"number","required":true}]}"""),
        new("play-sound", "Reproducir Sound Alert", "Uno de los que ya configuraste en el dashboard.",
            """{"fields":[{"key":"soundId","label":"Sonido","type":"select","dynamic":true,"required":true}]}""")
    ];

    // Nunca se dispara — este plugin no tiene estado de conexión ni eventos
    // del lado de Decatron para reportar, cada acción es un POST/GET suelto.
#pragma warning disable CS0067
    public event EventHandler<PluginEvent>? EventRaised;
#pragma warning restore CS0067

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _context = context;
        return Task.CompletedTask;
    }

    // Sin conexión persistente — cada acción es un request suelto autenticado
    // con el Bearer del momento, no hay nada que abrir/cerrar acá.
    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);
        if (token is null)
        {
            return PluginActionResult.Fail("Cuenta de Decatron no conectada — conectala en Ajustes.");
        }

        var parameters = JsonDocument.Parse(parametersJson).RootElement;

        return actionId switch
        {
            "send-chat-message" => await SendChatMessageAsync(token, parameters, ct),
            "set-category" => await SetCategoryAsync(token, parameters, ct),
            "set-title" => await SetTitleAsync(token, parameters, ct),
            "timer-start" => await TimerStartAsync(token, parameters, ct),
            "timer-pause" => await SimplePostAsync(token, "/timer/pause", "Timer pausado.", ct),
            "timer-resume" => await SimplePostAsync(token, "/timer/resume", "Timer reanudado.", ct),
            "timer-stop" => await SimplePostAsync(token, "/timer/stop", "Timer detenido.", ct),
            "timer-add" => await TimerAddAsync(token, parameters, ct),
            "play-sound" => await PlaySoundAsync(token, parameters, ct),
            _ => PluginActionResult.Fail($"Acción desconocida: '{actionId}'.")
        };
    }

    private async Task<PluginActionResult> SendChatMessageAsync(string token, JsonElement parameters, CancellationToken ct)
    {
        var message = parameters.GetProperty("message").GetString()!;
        var (ok, body) = await PostAsync(token, "/chat/send", new { message }, ct);
        return ok ? PluginActionResult.Ok("Mensaje enviado al chat.") : PluginActionResult.Fail($"Decatron rechazó el mensaje: {body}");
    }

    private async Task<PluginActionResult> SetCategoryAsync(string token, JsonElement parameters, CancellationToken ct)
    {
        var gameId = parameters.GetProperty("gameId").GetString()!;
        var (ok, body) = await PostAsync(token, "/twitch/category", new { gameId }, ct);
        return ok ? PluginActionResult.Ok("Categoría actualizada.") : PluginActionResult.Fail($"Decatron rechazó el cambio de categoría: {body}");
    }

    private async Task<PluginActionResult> SetTitleAsync(string token, JsonElement parameters, CancellationToken ct)
    {
        var title = parameters.GetProperty("title").GetString()!;
        var (ok, body) = await PostAsync(token, "/twitch/title", new { title }, ct);
        return ok ? PluginActionResult.Ok("Título actualizado.") : PluginActionResult.Fail($"Decatron rechazó el cambio de título: {body}");
    }

    private async Task<PluginActionResult> TimerStartAsync(string token, JsonElement parameters, CancellationToken ct)
    {
        var duration = parameters.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
            ? (int?)d.GetInt32()
            : null;
        var (ok, body) = await PostAsync(token, "/timer/start", new { duration }, ct);
        return ok ? PluginActionResult.Ok("Timer iniciado.") : PluginActionResult.Fail($"Decatron rechazó iniciar el timer: {body}");
    }

    private async Task<PluginActionResult> TimerAddAsync(string token, JsonElement parameters, CancellationToken ct)
    {
        var seconds = parameters.GetProperty("seconds").GetInt32();
        var (ok, body) = await PostAsync(token, "/timer/add", new { seconds }, ct);
        return ok ? PluginActionResult.Ok($"{seconds}s agregados al timer.") : PluginActionResult.Fail($"Decatron rechazó agregar tiempo: {body}");
    }

    private async Task<PluginActionResult> SimplePostAsync(string token, string path, string okMessage, CancellationToken ct)
    {
        var (ok, body) = await PostAsync(token, path, new { }, ct);
        return ok ? PluginActionResult.Ok(okMessage) : PluginActionResult.Fail($"Decatron rechazó la acción: {body}");
    }

    private async Task<PluginActionResult> PlaySoundAsync(string token, JsonElement parameters, CancellationToken ct)
    {
        var soundId = parameters.GetProperty("soundId").GetString()!;
        var (ok, body) = await PostAsync(token, "/sounds/play", new { soundId }, ct);
        return ok ? PluginActionResult.Ok("Sound Alert disparado.") : PluginActionResult.Fail($"Decatron rechazó reproducir el sonido: {body}");
    }

    // Lista fija (no búsqueda en vivo como categorías) — se trae una sola vez
    // al abrir el diálogo, la cantidad de Sound Alerts configurados es chica.
    public async Task<IReadOnlyList<ParameterOption>> GetParameterOptionsAsync(
        string actionId, string parameterKey, CancellationToken ct = default)
    {
        if ((actionId, parameterKey) != ("play-sound", "soundId"))
        {
            return [];
        }

        var token = await GetTokenAsync(ct);
        if (token is null) return [];

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBaseUrl}/sounds");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return [];

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("sounds").EnumerateArray()
                .Select(s => new ParameterOption(s.GetProperty("id").GetString()!, s.GetProperty("name").GetString()!))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ParameterOption>> SearchParameterOptionsAsync(
        string actionId, string parameterKey, string query, CancellationToken ct = default)
    {
        if ((actionId, parameterKey) != ("set-category", "gameId") || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var token = await GetTokenAsync(ct);
        if (token is null) return [];

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBaseUrl}/twitch/games/search?query={Uri.EscapeDataString(query)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return [];

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("games").EnumerateArray()
                .Select(g => new ParameterOption(g.GetProperty("id").GetString()!, g.GetProperty("name").GetString()!))
                .ToList();
        }
        catch
        {
            // Sin red, Decatron caído, lo que sea — el autocomplete se queda
            // vacío para esa búsqueda, no rompe el diálogo.
            return [];
        }
    }

    // Consumido por ActionExecutor (vía el delegado que arma DeckAppService)
    // para resolver {categoria}/{titulo}/{viewers}/{ultimo_seguidor} — no es
    // parte del contrato IPlugin porque es específico de Decatron, no algo
    // que todo plugin deba saber hacer.
    public async Task<IReadOnlyDictionary<string, string>> GetLiveVariablesAsync(CancellationToken ct = default)
    {
        var empty = new Dictionary<string, string>();

        var token = await GetTokenAsync(ct);
        if (token is null) return empty;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBaseUrl}/twitch/live-info");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return empty;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var values = new Dictionary<string, string>();

            if (root.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.String)
                values["{categoria}"] = category.GetString()!;
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                values["{titulo}"] = title.GetString()!;
            if (root.TryGetProperty("viewers", out var viewers) && viewers.ValueKind == JsonValueKind.Number)
                values["{viewers}"] = viewers.GetInt32().ToString();
            if (root.TryGetProperty("lastFollower", out var lastFollower) && lastFollower.ValueKind == JsonValueKind.String)
                values["{ultimo_seguidor}"] = lastFollower.GetString()!;

            return values;
        }
        catch
        {
            // Sin red, Decatron caído, stream offline sin datos, lo que sea —
            // esas variables se quedan sin reemplazar, no rompe la ejecución.
            return empty;
        }
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct) =>
        _context is null ? null : await _context.Credentials.GetAsync("access-token", ct);

    private async Task<(bool Ok, string Body)> PostAsync(string token, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}{path}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return (response.IsSuccessStatusCode, responseBody);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
