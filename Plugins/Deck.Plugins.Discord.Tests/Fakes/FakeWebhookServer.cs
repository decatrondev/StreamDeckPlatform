using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Deck.Plugins.Discord.Tests.Fakes;

public sealed class FakeWebhookServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<string> _receivedMessages = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public Uri WebhookUrl => new($"http://127.0.0.1:{Port}/webhook");
    public bool RejectNextRequest { get; set; }
    public IReadOnlyCollection<string> ReceivedMessages => _receivedMessages.ToArray();

    public FakeWebhookServer()
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
            if (RejectNextRequest)
            {
                RejectNextRequest = false;
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();

            using var doc = JsonDocument.Parse(body);
            _receivedMessages.Enqueue(doc.RootElement.GetProperty("content").GetString() ?? "");

            context.Response.StatusCode = 204;
            context.Response.Close();
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ya cerrada */ }
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
        await StopAsync();
        _listener.Close();
    }
}
