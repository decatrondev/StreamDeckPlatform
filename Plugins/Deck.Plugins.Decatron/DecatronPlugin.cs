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
// plugin, un solo login de Decatron alcanza para mandar mensajes al chat
// (vía twitch.decatron.net/api/v1/chat/send, que reenvía usando la conexión
// que el bot ya tiene abierta a cada canal — ver plan en
// Flowdeck.Web.Api/content/docs/02-integracion-bot-decatron/01-plan.md).
public sealed class DecatronPlugin : IPlugin
{
    public const string PluginId = "decatron";

    private const string DefaultChatSendUrl = "https://twitch.decatron.net/api/v1/chat/send";

    private readonly HttpClient _http;
    private readonly string _chatSendUrl;

    private IPluginContext? _context;

    public DecatronPlugin() : this(new HttpClient(), DefaultChatSendUrl)
    {
    }

    // Para tests: apuntar al servidor falso en vez de la API real.
    public DecatronPlugin(HttpClient http, string chatSendUrl = DefaultChatSendUrl)
    {
        _http = http;
        _chatSendUrl = chatSendUrl;
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
            """{"fields":[{"key":"message","label":"Mensaje","type":"text","required":true}]}""")
    ];

    // Nunca se dispara — este plugin no tiene estado de conexión ni eventos
    // del lado de Decatron para reportar, cada acción es un POST suelto.
#pragma warning disable CS0067
    public event EventHandler<PluginEvent>? EventRaised;
#pragma warning restore CS0067

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _context = context;
        return Task.CompletedTask;
    }

    // Sin conexión persistente — cada acción es un POST suelto autenticado
    // con el Bearer del momento, no hay nada que abrir/cerrar acá.
    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        if (actionId != "send-chat-message")
        {
            return PluginActionResult.Fail($"Acción desconocida: '{actionId}'.");
        }

        var token = _context is null ? null : await _context.Credentials.GetAsync("access-token", ct);
        if (token is null)
        {
            return PluginActionResult.Fail("Cuenta de Decatron no conectada — conectala en Ajustes.");
        }

        var message = JsonDocument.Parse(parametersJson).RootElement.GetProperty("message").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, _chatSendUrl)
        {
            Content = JsonContent.Create(new { message })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return PluginActionResult.Fail($"Decatron rechazó el mensaje ({(int)response.StatusCode}): {body}");
        }

        return PluginActionResult.Ok("Mensaje enviado al chat.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
