using System.Linq;
using System.Text.Json;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Plugins.Discord;

// Tercer plugin (Fase 5) — misma pieza del contrato (IPlugin), transporte
// totalmente distinto a los dos anteriores: named pipe en Windows, socket de
// dominio Unix en Linux/macOS, en vez de WebSocket (OBS) o HTTPS (Spotify).
// Valida que el patrón de Fase 1 (carga dinámica + aislamiento de errores) no
// asume ningún tipo de transporte en particular.
public sealed class DiscordPlugin : IPlugin
{
    public const string PluginId = "discord";

    private readonly DiscordIpcClient _ipc;
    private readonly DiscordWebhookClient _webhook;

    private IPluginContext? _context;

    public DiscordConnectionState ConnectionState => _ipc.State;

    public DiscordPlugin() : this("DISCORD_CLIENT_ID_NOT_CONFIGURED")
    {
    }

    // pipeIndex/unixSocketDirOverride existen para que los tests apunten a un
    // servidor IPC falso en vez de buscar el Discord real de la máquina.
    public DiscordPlugin(string clientId, int pipeIndex = 0, string? unixSocketDirOverride = null, HttpClient? httpClient = null)
    {
        _ipc = new DiscordIpcClient(clientId, pipeIndex, unixSocketDirOverride);
        _ipc.EventReceived += OnIpcEvent;
        _ipc.StateChanged += OnConnectionStateChanged;
        _webhook = new DiscordWebhookClient(httpClient ?? new HttpClient());
    }

    public PluginMetadata Metadata { get; } = new(
        Id: PluginId,
        Name: "Discord",
        Version: "1.0.0",
        Author: "Flowdeck",
        Permissions: ["network", "local-ipc"]);

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("toggle-mute", "Mutear/desmutear micrófono"),
        new("set-voice-channel", "Cambiar de canal de voz", "Parámetro: channelId.",
            """{"fields":[{"key":"channelId","label":"ID del canal de voz","type":"text","required":true}]}"""),
        new("send-message", "Enviar mensaje rápido", "Parámetro: content. Requiere un webhook configurado.",
            """{"fields":[{"key":"content","label":"Mensaje","type":"text","required":true}]}""")
    ];

    public event EventHandler<PluginEvent>? EventRaised;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _ipc.Start();

        // Igual que en OBS: no se espera activamente a que quede conectado —
        // el propio cliente reintenta solo en background. Quien necesite
        // saber cuándo se conectó, escucha el evento "connection-state".
        await Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default) => _ipc.StopAsync();

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        try
        {
            var parameters = string.IsNullOrWhiteSpace(parametersJson) ? default : JsonDocument.Parse(parametersJson).RootElement;

            return actionId switch
            {
                "toggle-mute" => await ToggleMuteAsync(ct),
                "set-voice-channel" => await SetVoiceChannelAsync(parameters, ct),
                "send-message" => await SendMessageAsync(parameters, ct),
                _ => PluginActionResult.Fail($"Acción desconocida: '{actionId}'.")
            };
        }
        catch (InvalidOperationException ex)
        {
            return PluginActionResult.Fail($"Discord no está conectado: {ex.Message}");
        }
        catch (DiscordRpcException ex)
        {
            return PluginActionResult.Fail($"Discord rechazó el comando (código {ex.Code}): {ex.Message}");
        }
        catch (DiscordWebhookException ex)
        {
            return PluginActionResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Error inesperado hablando con Discord: {ex.Message}");
        }
    }

    private async Task<PluginActionResult> ToggleMuteAsync(CancellationToken ct)
    {
        var current = await _ipc.SendCommandAsync("GET_VOICE_SETTINGS", null, ct);
        var isMuted = current.GetProperty("mute").GetBoolean();

        await _ipc.SendCommandAsync("SET_VOICE_SETTINGS", new { mute = !isMuted }, ct);

        return PluginActionResult.Ok(isMuted ? "Micrófono desmuteado." : "Micrófono muteado.");
    }

    private async Task<PluginActionResult> SetVoiceChannelAsync(JsonElement parameters, CancellationToken ct)
    {
        var channelId = parameters.GetProperty("channelId").GetString()!;
        await _ipc.SendCommandAsync("SELECT_VOICE_CHANNEL", new { channel_id = channelId, force = true }, ct);
        return PluginActionResult.Ok($"Canal de voz cambiado.");
    }

    private async Task<PluginActionResult> SendMessageAsync(JsonElement parameters, CancellationToken ct)
    {
        if (_context is null) return PluginActionResult.Fail("Plugin no inicializado.");

        var webhookUrl = await _context.Credentials.GetAsync("webhook-url", ct);
        if (webhookUrl is null)
        {
            return PluginActionResult.Fail("No hay ningún webhook de Discord configurado para este botón.");
        }

        var content = parameters.GetProperty("content").GetString()!;
        await _webhook.SendMessageAsync(webhookUrl, content, ct);

        return PluginActionResult.Ok("Mensaje enviado.");
    }

    // Rich Presence — separado de ExecuteActionAsync porque no lo dispara un
    // botón del Stream Deck, lo llama DiscordRichPresenceService en un loop
    // propio mientras el streamer está en vivo (ver DeckAppService).
    public Task SetActivityAsync(DiscordActivity activity, CancellationToken ct = default) =>
        _ipc.SendCommandAsync("SET_ACTIVITY", new
        {
            pid = Environment.ProcessId,
            activity = new
            {
                details = activity.Details,
                state = activity.State,
                assets = new
                {
                    large_image = activity.LargeImageKey,
                    large_text = activity.LargeImageText,
                    small_image = activity.SmallImageKey,
                    small_text = activity.SmallImageText
                },
                buttons = activity.Buttons?.Select(b => new { label = b.Label, url = b.Url }).ToArray()
            }
        }, ct);

    // activity:null es como Discord pide "borrar" el Rich Presence actual —
    // no hay un comando CLEAR_ACTIVITY aparte.
    public Task ClearActivityAsync(CancellationToken ct = default) =>
        _ipc.SendCommandAsync("SET_ACTIVITY", new { pid = Environment.ProcessId, activity = (object?)null }, ct);

    private void OnIpcEvent(string evt, JsonElement data) =>
        EventRaised?.Invoke(this, new PluginEvent(
            evt == "VOICE_STATE_UPDATE" ? "voice-state-update" : evt,
            data.ValueKind == JsonValueKind.Undefined ? "{}" : data.GetRawText(),
            DateTimeOffset.UtcNow));

    private void OnConnectionStateChanged(DiscordConnectionState state) =>
        EventRaised?.Invoke(this, new PluginEvent(
            "connection-state",
            JsonSerializer.Serialize(new { state = state.ToString() }),
            DateTimeOffset.UtcNow));

    public async ValueTask DisposeAsync()
    {
        _ipc.EventReceived -= OnIpcEvent;
        _ipc.StateChanged -= OnConnectionStateChanged;
        await _ipc.DisposeAsync();
    }
}
