using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deck.Plugins.Obs;

// Cliente del protocolo obs-websocket v5 (JSON sobre WebSocket). No depende de
// ningún paquete de terceros — el protocolo es simple y así se evita quedar
// atado a una librería que puede no seguir la versión 5 de OBS.
//
// Maneja la reconexión automática por su cuenta (el Core nunca reintenta por
// un plugin, es responsabilidad del propio plugin — ver Deck.SDK.IPlugin).
// Una falla de autenticación NO reintenta sola: se corta el loop y queda en
// AuthenticationFailed hasta que alguien llame StartAsync de nuevo con una
// contraseña distinta.
public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private const int AuthFailedCloseCode = 4009;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifecycleCts;
    private Task? _loopTask;
    private Uri _uri = null!;
    private string? _password;
    private bool _hasEverConnected;

    public ObsConnectionState State { get; private set; } = ObsConnectionState.Disconnected;

    public event Action<ObsConnectionState>? StateChanged;
    public event Action<string, JsonElement>? EventReceived;

    public void Start(Uri uri, string? password)
    {
        _uri = uri;
        _password = password;
        _hasEverConnected = false;
        _lifecycleCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_lifecycleCts.Token));
    }

    public async Task StopAsync()
    {
        _lifecycleCts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch { /* el loop ya maneja sus propias excepciones */ }
        }

        await CloseSocketAsync();
        SetState(ObsConnectionState.Disconnected);
    }

    public async Task<JsonElement> SendRequestAsync(string requestType, object? requestData, CancellationToken ct)
    {
        var socket = _socket;
        if (socket is null || State != ObsConnectionState.Connected)
        {
            throw new InvalidOperationException("No hay conexión activa con OBS.");
        }

        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        await using var registration = ct.Register(() => tcs.TrySetCanceled(ct));

        var payload = requestData is null
            ? (object)new { requestType, requestId }
            : new { requestType, requestId, requestData };

        await SendAsync(socket, new { op = ObsOpCode.Request, d = payload }, ct);

        return await tcs.Task;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(_hasEverConnected ? ObsConnectionState.Reconnecting : ObsConnectionState.Connecting);
                await ConnectAndIdentifyAsync(ct);

                _hasEverConnected = true;
                SetState(ObsConnectionState.Connected);

                await ReceiveLoopAsync(ct);

                // ReceiveLoopAsync vuelve sin excepción cuando el otro lado
                // cerró prolijo — hay que marcarlo caído YA, no recién al
                // arrancar el próximo intento: si no, un pedido que llega
                // durante la espera de reconexión ve "Connected" (estado
                // viejo) y trata de mandarse por un socket que ya no sirve.
                if (!ct.IsCancellationRequested) SetState(ObsConnectionState.Reconnecting);
            }
            catch (ObsAuthenticationException)
            {
                FailPendingRequests();
                SetState(ObsConnectionState.AuthenticationFailed);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                FailPendingRequests();
                return;
            }
            catch
            {
                // Red caída, OBS cerrado, etc. — se resuelve reintentando abajo.
                if (!ct.IsCancellationRequested) SetState(ObsConnectionState.Reconnecting);
            }

            FailPendingRequests();

            if (ct.IsCancellationRequested) return;

            try
            {
                await Task.Delay(ReconnectDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAndIdentifyAsync(CancellationToken ct)
    {
        await CloseSocketAsync();

        var socket = new ClientWebSocket();
        // OBS suele correr en la misma máquina o en la LAN — un keepalive
        // corto detecta una caída de red real (no solo un cierre prolijo)
        // sin esperar los minutos que tardaría el timeout default del SO.
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
        await socket.ConnectAsync(_uri, ct);
        _socket = socket;

        var hello = await ReceiveJsonAsync(socket, ct)
            ?? throw new IOException("OBS cerró la conexión antes de saludar.");

        var helloData = hello.GetProperty("d");

        object identifyD = helloData.TryGetProperty("authentication", out var authEl)
            ? new
            {
                rpcVersion = 1,
                authentication = ComputeAuthString(
                    _password ?? throw new ObsAuthenticationException("OBS requiere contraseña y no se configuró ninguna."),
                    authEl.GetProperty("salt").GetString()!,
                    authEl.GetProperty("challenge").GetString()!),
                eventSubscriptions = int.MaxValue
            }
            : new { rpcVersion = 1, eventSubscriptions = int.MaxValue };

        await SendAsync(socket, new { op = ObsOpCode.Identify, d = identifyD }, ct);

        var identified = await ReceiveJsonAsync(socket, ct);

        if (identified is null)
        {
            if ((int)socket.CloseStatus! == AuthFailedCloseCode)
            {
                throw new ObsAuthenticationException("OBS rechazó la contraseña.");
            }
            throw new IOException("OBS cerró la conexión durante la identificación.");
        }

        if (identified.Value.GetProperty("op").GetInt32() != ObsOpCode.Identified)
        {
            throw new IOException("Respuesta inesperada de OBS durante la identificación.");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var socket = _socket!;

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var message = await ReceiveJsonAsync(socket, ct);
            if (message is null) return; // conexión cerrada por el otro lado

            var op = message.Value.GetProperty("op").GetInt32();
            var d = message.Value.GetProperty("d");

            switch (op)
            {
                case ObsOpCode.Event:
                    var eventType = d.GetProperty("eventType").GetString()!;
                    var eventData = d.TryGetProperty("eventData", out var ed) ? ed : default;
                    EventReceived?.Invoke(eventType, eventData);
                    break;

                case ObsOpCode.RequestResponse:
                    HandleRequestResponse(d);
                    break;
            }
        }
    }

    private void HandleRequestResponse(JsonElement d)
    {
        var requestId = d.GetProperty("requestId").GetString()!;
        if (!_pending.TryRemove(requestId, out var tcs)) return;

        var status = d.GetProperty("requestStatus");
        var success = status.GetProperty("result").GetBoolean();

        if (!success)
        {
            var code = status.GetProperty("code").GetInt32();
            var comment = status.TryGetProperty("comment", out var c) ? c.GetString() ?? "" : "";
            tcs.TrySetException(new ObsRequestException(code, comment));
            return;
        }

        var responseData = d.TryGetProperty("responseData", out var rd) ? rd : default;
        tcs.TrySetResult(responseData);
    }

    private void FailPendingRequests()
    {
        foreach (var key in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(key, out var tcs))
            {
                tcs.TrySetException(new IOException("Se perdió la conexión con OBS antes de recibir respuesta."));
            }
        }
    }

    private void SetState(ObsConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static string ComputeAuthString(string password, string salt, string challenge)
    {
        var secret = Sha256Base64(password + salt);
        return Sha256Base64(secret + challenge);
    }

    private static string Sha256Base64(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static async Task SendAsync(ClientWebSocket socket, object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    // Devuelve null si el socket se cerró en vez de mandar un mensaje.
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

            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Completa el handshake de cierre en espejo — si no, el otro
                // lado (OBS, o nuestro servidor falso de test) puede quedarse
                // esperando la confirmación de cierre.
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch { /* ya se estaba cerrando */ }
                return null;
            }

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
                // Timeout defensivo: un plugin jamás debe poder trabar al
                // Core esperando un cierre de socket que nunca llega.
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeoutCts.Token);
            }
        }
        catch
        {
            // ya se estaba cerrando, cayó, o venció el timeout — no hay nada más que hacer.
        }
        finally
        {
            _socket.Dispose();
            _socket = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleCts?.Dispose();
    }
}
