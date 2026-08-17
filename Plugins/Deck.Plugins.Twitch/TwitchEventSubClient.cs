using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Deck.Plugins.Twitch;

// EventSub WebSocket (wss://eventsub.wss.twitch.tv/ws): la parte "más
// exigente en tiempo real" del roadmap. A diferencia de OBS/Discord, acá el
// servidor manda un "session_welcome" con un session_id que hay que usar para
// crear las suscripciones por REST (ver TwitchApiClient), y después manda
// "session_keepalive" cada tantos segundos — si esos keepalives dejan de
// llegar, la conexión está muerta aunque el socket no haya avisado (mismo
// problema que ya nos mordió con el cierre abrupto de OBS en Fase 3, acá
// Twitch directamente lo hace parte del protocolo en vez de dejarlo a
// TCP/keepalive de más bajo nivel).
//
// Simplificación consciente: ante un "session_reconnect", en vez de abrir la
// conexión nueva al reconnect_url específico y migrar en caliente (el
// procedimiento "perfecto" de Twitch), simplemente se trata como una caída
// más y se reconecta por el loop normal. Se pierden unos segundos de eventos
// en el peor caso, pero el comportamiento es muchísimo más simple de razonar
// y de probar.
public sealed class TwitchEventSubClient : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly Uri _uri;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifecycleCts;
    private Task? _loopTask;
    private bool _hasEverConnected;
    private TimeSpan _keepaliveTimeout = TimeSpan.FromSeconds(10);

    public TwitchConnectionState State { get; private set; } = TwitchConnectionState.Disconnected;
    public string? SessionId { get; private set; }

    public event Action<TwitchConnectionState>? StateChanged;
    public event Action<string, JsonElement>? NotificationReceived;

    public TwitchEventSubClient(Uri? uri = null)
    {
        _uri = uri ?? new Uri("wss://eventsub.wss.twitch.tv/ws");
    }

    public void Start()
    {
        _hasEverConnected = false;
        _lifecycleCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_lifecycleCts.Token));
    }

    public async Task StopAsync()
    {
        _lifecycleCts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch { /* el loop maneja sus propias excepciones */ }
        }

        await CloseSocketAsync();
        SetState(TwitchConnectionState.Disconnected);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(_hasEverConnected ? TwitchConnectionState.Reconnecting : TwitchConnectionState.Connecting);
                await ConnectAndWaitWelcomeAsync(ct);

                _hasEverConnected = true;
                SetState(TwitchConnectionState.Connected);

                await ReceiveLoopAsync(ct);

                if (!ct.IsCancellationRequested) SetState(TwitchConnectionState.Reconnecting);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                if (!ct.IsCancellationRequested) SetState(TwitchConnectionState.Reconnecting);
            }

            SessionId = null;

            if (ct.IsCancellationRequested) return;

            try { await Task.Delay(ReconnectDelay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConnectAndWaitWelcomeAsync(CancellationToken ct)
    {
        await CloseSocketAsync();

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(_uri, ct);
        _socket = socket;

        var message = await ReceiveJsonAsync(socket, ct)
            ?? throw new IOException("Twitch cerró la conexión antes de mandar session_welcome.");

        var metadata = message.GetProperty("metadata");
        if (metadata.GetProperty("message_type").GetString() != "session_welcome")
        {
            throw new IOException("Twitch no mandó session_welcome como primer mensaje.");
        }

        var session = message.GetProperty("payload").GetProperty("session");
        SessionId = session.GetProperty("id").GetString();

        if (session.TryGetProperty("keepalive_timeout_seconds", out var kt) && kt.ValueKind == JsonValueKind.Number)
        {
            _keepaliveTimeout = TimeSpan.FromSeconds(kt.GetInt32());
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var socket = _socket!;

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            // Sin esto, una conexión que se cae sin avisar (sin frame de
            // cierre) se queda esperando datos que nunca llegan — el mismo
            // problema que ya resolvimos para OBS, pero acá Twitch lo hace
            // parte explícita del protocolo vía keepalive.
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idleCts.CancelAfter(_keepaliveTimeout * 2);

            JsonElement? message;
            try
            {
                message = await ReceiveJsonAsync(socket, idleCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException("No llegó ningún keepalive de Twitch a tiempo — se asume la conexión caída.");
            }

            if (message is null) return; // conexión cerrada por el otro lado

            HandleMessage(message.Value);
        }
    }

    private void HandleMessage(JsonElement message)
    {
        var messageType = message.GetProperty("metadata").GetProperty("message_type").GetString();
        var payload = message.GetProperty("payload");

        switch (messageType)
        {
            case "session_keepalive":
                break; // solo sirve para saber que sigue viva — nada que hacer

            case "notification":
                var subscriptionType = payload.GetProperty("subscription").GetProperty("type").GetString()!;
                var eventData = payload.GetProperty("event");
                NotificationReceived?.Invoke(subscriptionType, eventData.Clone());
                break;

            case "session_reconnect":
            case "revocation":
                // Se deja que el loop principal lo trate como una caída
                // normal — ver comentario de clase sobre la simplificación.
                throw new IOException($"Twitch mandó '{messageType}' — reconectando.");
        }
    }

    private void SetState(TwitchConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static async Task<JsonElement?> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close) return null;

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }

        stream.Position = 0;
        return JsonSerializer.Deserialize<JsonElement>(stream);
    }

    private async Task CloseSocketAsync()
    {
        if (_socket is null) return;

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeoutCts.Token);
            }
        }
        catch { /* ya se estaba cerrando, cayó, o venció el timeout */ }
        finally
        {
            _socket.Dispose();
            _socket = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
