using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

namespace Deck.Plugins.Twitch.Tests.Fakes;

// WebSocket que manda el session_welcome inicial y después queda mudo salvo
// que el test le pida mandar un keepalive o una notificación — así se
// controla con precisión qué ve el cliente en cada momento del test.
public sealed class FakeTwitchEventSubServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentBag<WebSocket> _sockets = [];
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public Uri Uri => new($"ws://127.0.0.1:{Port}/ws");
    public int KeepaliveTimeoutSeconds { get; set; } = 10;
    public string SessionId { get; } = $"session-{Guid.NewGuid():N}";

    public FakeTwitchEventSubServer()
    {
        Port = GetFreeTcpPort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

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
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* cancelado o timeout */ }
        }
    }

    public void DropConnection()
    {
        while (_sockets.TryTake(out var socket))
        {
            try { socket.Abort(); } catch { /* ya cerrado */ }
        }
    }

    public Task SendNotificationAsync(string subscriptionType, object eventData, CancellationToken ct = default) =>
        BroadcastAsync(new
        {
            metadata = new { message_id = Guid.NewGuid().ToString(), message_type = "notification" },
            payload = new { subscription = new { type = subscriptionType }, @event = eventData }
        }, ct);

    public Task SendKeepaliveAsync(CancellationToken ct = default) =>
        BroadcastAsync(new
        {
            metadata = new { message_id = Guid.NewGuid().ToString(), message_type = "session_keepalive" },
            payload = new { }
        }, ct);

    private async Task BroadcastAsync(object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        foreach (var socket in _sockets)
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(ct); }
            catch { return; }

            _ = Task.Run(() => HandleConnectionAsync(context, ct), ct);
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
            var welcome = new
            {
                metadata = new { message_id = Guid.NewGuid().ToString(), message_type = "session_welcome" },
                payload = new
                {
                    session = new
                    {
                        id = SessionId,
                        status = "connected",
                        keepalive_timeout_seconds = KeepaliveTimeoutSeconds,
                        reconnect_url = (string?)null,
                        connected_at = DateTimeOffset.UtcNow.ToString("O")
                    }
                }
            };

            var bytes = JsonSerializer.SerializeToUtf8Bytes(welcome);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

            // Se queda escuchando (el cliente no manda nada por acá, pero hay
            // que seguir el ciclo de vida del socket para notar si se cae).
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
            }
        }
        catch
        {
            // conexión cortada — nada que hacer.
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
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
