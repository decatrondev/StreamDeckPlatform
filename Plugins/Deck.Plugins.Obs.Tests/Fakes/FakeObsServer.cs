using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deck.Plugins.Obs.Tests.Fakes;

// Implementa el subconjunto del protocolo obs-websocket v5 que necesita el
// plugin real: Hello/Identify/Identified (con o sin contraseña), Request/
// RequestResponse, Event, y la posibilidad de cortar la conexión de golpe
// para simular que OBS se cerró (valida la reconexión automática del
// checklist de "plugin listo").
public sealed class FakeObsServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentBag<WebSocket> _sockets = [];
    private readonly ConcurrentBag<Task> _connectionHandlers = [];
    private readonly ConcurrentQueue<(string RequestType, JsonElement? RequestData)> _receivedRequests = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public string? RequiredPassword { get; set; }
    public IReadOnlyCollection<(string RequestType, JsonElement? RequestData)> ReceivedRequests => _receivedRequests.ToArray();

    // Por default cualquier request responde con {} — algunos tests necesitan
    // simular una respuesta real (ej. GetSceneList con escenas falsas) para
    // validar cómo el plugin la interpreta.
    public Dictionary<string, object> CannedResponses { get; } = new();

    public FakeObsServer()
    {
        Port = GetFreeTcpPort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public Uri Uri => new($"ws://127.0.0.1:{Port}/");

    public Task StartAsync()
    {
        _listener.Start();
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener.Stop();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* cancelado a propósito, o timeout */ }
        }

        // Bounded: si algún handler quedó atascado, no nos lleva puestos —
        // los tests que crean un servidor por prueba no pueden depender de
        // que TODOS los handlers terminen prolijo para poder seguir.
        try
        {
            await Task.WhenAll(_connectionHandlers).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // timeout o alguno falló — no bloquea el DisposeAsync del test.
        }
    }

    // Simula "se cerró OBS": manda el frame de cierre de WebSocket normal, tal
    // como haría la app real al apagarse (a diferencia de un cable de red
    // cortado, que a nivel TCP puede tardar mucho más en notarse sin
    // keepalive — no es lo que este test necesita validar). El listener sigue
    // vivo, así que una reconexión posterior del cliente vuelve a funcionar
    // (como si el usuario hubiera vuelto a abrir OBS).
    public async Task CloseAllConnectionsAsync()
    {
        while (_sockets.TryTake(out var socket))
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "simulated obs close", timeoutCts.Token);
                }
            }
            catch
            {
                // ya estaba cerrado — no hay nada más que hacer.
            }
        }
    }

    public async Task BroadcastEventAsync(string eventType, object eventData)
    {
        var message = new { op = ObsOpCode.Event, d = new { eventType, eventData } };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);

        foreach (var socket in _sockets)
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }

            _connectionHandlers.Add(Task.Run(() => HandleConnectionAsync(context, ct), ct));
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        var wsContext = await context.AcceptWebSocketAsync(null);
        var socket = wsContext.WebSocket;
        _sockets.Add(socket);

        try
        {
            if (!await DoHandshakeAsync(socket, ct)) return;
            await RequestLoopAsync(socket, ct);
        }
        catch
        {
            // Conexión cortada (a propósito o no) — nada que reportar acá, el
            // cliente es quien decide qué hacer con eso.
        }
    }

    private async Task<bool> DoHandshakeAsync(WebSocket socket, CancellationToken ct)
    {
        string? salt = null, challenge = null;
        object helloD;

        if (RequiredPassword is not null)
        {
            salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            helloD = new { obsWebSocketVersion = "5.0.0", rpcVersion = 1, authentication = new { challenge, salt } };
        }
        else
        {
            helloD = new { obsWebSocketVersion = "5.0.0", rpcVersion = 1 };
        }

        await SendAsync(socket, new { op = ObsOpCode.Hello, d = helloD }, ct);

        var identify = await ReceiveJsonAsync(socket, ct);
        if (identify is null) return false;

        if (RequiredPassword is not null)
        {
            var provided = identify.Value.GetProperty("d").TryGetProperty("authentication", out var a) ? a.GetString() : null;
            var expected = ComputeAuthString(RequiredPassword, salt!, challenge!);

            if (provided != expected)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
                try { await socket.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed", timeoutCts.Token); }
                catch { /* el cliente puede no llegar a confirmar el cierre — no bloquea */ }
                return false;
            }
        }

        await SendAsync(socket, new { op = ObsOpCode.Identified, d = new { negotiatedRpcVersion = 1 } }, ct);
        return true;
    }

    private async Task RequestLoopAsync(WebSocket socket, CancellationToken ct)
    {
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var message = await ReceiveJsonAsync(socket, ct);
            if (message is null) return;

            var d = message.Value.GetProperty("d");
            var requestType = d.GetProperty("requestType").GetString()!;
            var requestId = d.GetProperty("requestId").GetString()!;
            var requestData = d.TryGetProperty("requestData", out var rd) ? rd : (JsonElement?)null;

            _receivedRequests.Enqueue((requestType, requestData));

            var responseData = CannedResponses.TryGetValue(requestType, out var canned) ? canned : new { };
            var response = new
            {
                op = ObsOpCode.RequestResponse,
                d = new
                {
                    requestType,
                    requestId,
                    requestStatus = new { result = true, code = 100 },
                    responseData
                }
            };

            await SendAsync(socket, response, ct);
        }
    }

    private static string ComputeAuthString(string password, string salt, string challenge)
    {
        var secret = Sha256Base64(password + salt);
        return Sha256Base64(secret + challenge);
    }

    private static string Sha256Base64(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static async Task SendAsync(WebSocket socket, object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static async Task<JsonElement?> ReceiveJsonAsync(WebSocket socket, CancellationToken ct)
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
            catch
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

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _listener.Close();
    }
}
