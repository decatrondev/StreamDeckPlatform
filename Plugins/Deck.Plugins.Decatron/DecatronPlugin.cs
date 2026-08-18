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
            """{"fields":[{"key":"title","label":"Título","type":"text","required":true}]}""")
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
