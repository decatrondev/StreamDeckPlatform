using System.Text.Json;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Plugins.Obs;

// Primer plugin real (Fase 3) — valida el contrato de Deck.SDK definido en
// Fase 1. Sin OAuth a propósito: obs-websocket usa como mucho una contraseña
// simple, ideal para probar el patrón de Credential Manager antes de meterse
// con OAuth real (Fase 4, Spotify).
public sealed class ObsPlugin : IPlugin
{
    public const string PluginId = "obs";

    // Host/puerto quedan fijos al default de obs-websocket — no son secretos,
    // así que no pasan por el Credential Manager. Si más adelante hace falta
    // configurarlos, es un setting de plugin, no una credencial.
    private readonly Uri _uri;
    private readonly ObsWebSocketClient _client = new();

    private IPluginContext? _context;

    public ObsPlugin() : this(new Uri("ws://127.0.0.1:4455"))
    {
    }

    // Para tests: apuntar al servidor OBS falso en vez del puerto default.
    public ObsPlugin(Uri uri)
    {
        _uri = uri;
    }

    // Expuesto para que la UI pueda mostrar "OBS: conectado / reconectando /
    // desconectado" sin tener que inferirlo del resultado de la última acción.
    public ObsConnectionState ConnectionState => _client.State;

    public PluginMetadata Metadata { get; } = new(
        Id: PluginId,
        Name: "OBS Studio",
        Version: "1.0.0",
        Author: "Flowdeck",
        Permissions: ["network"]);

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("set-scene", "Cambiar escena", "Cambia la escena de programa actual.",
            """{"fields":[{"key":"scene","label":"Escena","type":"select","dynamic":true,"required":true}]}"""),
        new("toggle-mute", "Mutear/desmutear fuente", "Alterna el mute de una fuente de audio.",
            """{"fields":[{"key":"source","label":"Fuente de audio","type":"select","dynamic":true,"required":true}]}"""),
        new("start-stream", "Iniciar stream"),
        new("stop-stream", "Detener stream"),
        new("start-record", "Iniciar grabación"),
        new("stop-record", "Detener grabación")
    ];

    public event EventHandler<PluginEvent>? EventRaised;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _context = context;
        _client.EventReceived += OnObsEvent;
        _client.StateChanged += OnConnectionStateChanged;
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var password = await GetPasswordAsync(ct);
        _client.Start(_uri, password);

        // No hace esperar la conexión — el propio cliente reintenta solo en
        // background (reconexión automática, sección 5 del contrato). Si el
        // llamador quiere saber cuándo quedó conectado, se suscribe al evento
        // "connection-state".
        await Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default) => _client.StopAsync();

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        try
        {
            var parameters = string.IsNullOrWhiteSpace(parametersJson) ? default : JsonDocument.Parse(parametersJson).RootElement;

            return actionId switch
            {
                "set-scene" => await SetSceneAsync(parameters, ct),
                "toggle-mute" => await ToggleMuteAsync(parameters, ct),
                "start-stream" => await SimpleRequestAsync("StartStream", ct),
                "stop-stream" => await SimpleRequestAsync("StopStream", ct),
                "start-record" => await SimpleRequestAsync("StartRecord", ct),
                "stop-record" => await SimpleRequestAsync("StopRecord", ct),
                _ => PluginActionResult.Fail($"Acción desconocida: '{actionId}'.")
            };
        }
        catch (InvalidOperationException ex)
        {
            return PluginActionResult.Fail($"OBS no está conectado: {ex.Message}");
        }
        catch (ObsRequestException ex)
        {
            return PluginActionResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Error inesperado hablando con OBS: {ex.Message}");
        }
    }

    private async Task<PluginActionResult> SetSceneAsync(JsonElement parameters, CancellationToken ct)
    {
        var scene = parameters.GetProperty("scene").GetString()!;
        await _client.SendRequestAsync("SetCurrentProgramScene", new { sceneName = scene }, ct);
        return PluginActionResult.Ok($"Escena cambiada a '{scene}'.");
    }

    private async Task<PluginActionResult> ToggleMuteAsync(JsonElement parameters, CancellationToken ct)
    {
        var source = parameters.GetProperty("source").GetString()!;
        await _client.SendRequestAsync("ToggleInputMute", new { inputName = source }, ct);
        return PluginActionResult.Ok($"'{source}' mute alternado.");
    }

    private async Task<PluginActionResult> SimpleRequestAsync(string requestType, CancellationToken ct)
    {
        await _client.SendRequestAsync(requestType, null, ct);
        return PluginActionResult.Ok($"{requestType} ejecutado.");
    }

    public async Task<IReadOnlyList<ParameterOption>> GetParameterOptionsAsync(
        string actionId, string parameterKey, CancellationToken ct = default)
    {
        try
        {
            return (actionId, parameterKey) switch
            {
                ("set-scene", "scene") => await GetSceneOptionsAsync(ct),
                ("toggle-mute", "source") => await GetInputOptionsAsync(ct),
                _ => []
            };
        }
        catch
        {
            // OBS desconectado, timeout, lo que sea — el campo dinámico queda
            // vacío en la UI, no rompe el diálogo de asignar tecla.
            return [];
        }
    }

    private async Task<IReadOnlyList<ParameterOption>> GetSceneOptionsAsync(CancellationToken ct)
    {
        var response = await _client.SendRequestAsync("GetSceneList", null, ct);
        return response.GetProperty("scenes").EnumerateArray()
            .Select(s => s.GetProperty("sceneName").GetString()!)
            .Select(name => new ParameterOption(name, name))
            .ToList();
    }

    private async Task<IReadOnlyList<ParameterOption>> GetInputOptionsAsync(CancellationToken ct)
    {
        // Se listan todos los inputs, no solo los de audio — OBS no expone un
        // filtro genérico simple para "solo audio" y llamar toggle-mute sobre
        // algo sin audio no hace nada, no rompe.
        var response = await _client.SendRequestAsync("GetInputList", null, ct);
        return response.GetProperty("inputs").EnumerateArray()
            .Select(i => i.GetProperty("inputName").GetString()!)
            .Select(name => new ParameterOption(name, name))
            .ToList();
    }

    private async Task<string?> GetPasswordAsync(CancellationToken ct) =>
        _context is null ? null : await _context.Credentials.GetAsync("password", ct);

    private void OnObsEvent(string eventType, JsonElement eventData)
    {
        var mapped = eventType switch
        {
            "StreamStateChanged" => "stream-state",
            "RecordStateChanged" => "record-state",
            _ => null
        };

        if (mapped is null) return;

        EventRaised?.Invoke(this, new PluginEvent(mapped, eventData.GetRawText(), DateTimeOffset.UtcNow));
    }

    private void OnConnectionStateChanged(ObsConnectionState state)
    {
        EventRaised?.Invoke(this, new PluginEvent(
            "connection-state",
            JsonSerializer.Serialize(new { state = state.ToString() }),
            DateTimeOffset.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        _client.EventReceived -= OnObsEvent;
        _client.StateChanged -= OnConnectionStateChanged;
        await _client.DisposeAsync();
    }
}
