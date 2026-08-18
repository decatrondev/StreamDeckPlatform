using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Deck.Plugins.Decatron.Tests.Fakes;

// Simula el único endpoint que este plugin llama (POST /api/v1/chat/send de
// twitch.decatron.net) — no hace falta levantar el bot real para probar el
// plugin.
public sealed class FakeDecatronApiServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public string ChatSendUrl => $"http://127.0.0.1:{Port}/api/v1/chat/send";

    public string ExpectedToken { get; set; } = "test-access-token";
    public bool RejectRequest { get; set; }
    public string? LastReceivedMessage { get; private set; }

    public FakeDecatronApiServer()
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

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(ct); }
            catch { return; }

            _ = Task.Run(() => HandleAsync(context), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var authHeader = context.Request.Headers["Authorization"];
            if (RejectRequest || authHeader != $"Bearer {ExpectedToken}")
            {
                context.Response.StatusCode = authHeader != $"Bearer {ExpectedToken}" ? 401 : 400;
                context.Response.Close();
                return;
            }

            var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);
            LastReceivedMessage = doc.RootElement.GetProperty("message").GetString();

            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { success = true, message = "Message sent" });
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ya se habrá cerrado */ }
        }
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
        _cts?.Cancel();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* cancelado o timeout */ }
        }
        _listener.Close();
    }
}
