using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Deck.Plugins.Decatron.Tests.Fakes;

// Simula los endpoints que este plugin llama en twitch.decatron.net
// (chat/send, twitch/category, twitch/title, twitch/games/search) — no hace
// falta levantar el bot real para probar el plugin.
public sealed class FakeDecatronApiServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}/api/v1";

    public string ExpectedToken { get; set; } = "test-access-token";
    public bool RejectRequest { get; set; }
    public string? LastReceivedMessage { get; private set; }
    public string? LastReceivedGameId { get; private set; }
    public string? LastReceivedTitle { get; private set; }
    public List<(string Id, string Name)> GameSearchResults { get; set; } = [];
    public bool IsLive { get; set; } = true;
    public string? LiveCategory { get; set; } = "Just Chatting";
    public string? LiveTitle { get; set; } = "probando Flowdeck";
    public int? LiveViewers { get; set; } = 42;
    public string? LastFollower { get; set; } = "un_seguidor_cualquiera";
    public List<string> ReceivedTimerCalls { get; } = [];
    public int? LastReceivedDuration { get; private set; }
    public int? LastReceivedSeconds { get; private set; }
    public List<(string Id, string Name)> Sounds { get; set; } = [];
    public string? LastReceivedSoundId { get; private set; }

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
                WriteJson(context, authHeader != $"Bearer {ExpectedToken}" ? 401 : 400, new { error = "unauthorized" });
                return;
            }

            var path = context.Request.Url!.AbsolutePath;

            if (path == "/api/v1/chat/send")
            {
                var doc = await ReadJsonBodyAsync(context);
                LastReceivedMessage = doc.RootElement.GetProperty("message").GetString();
                WriteJson(context, 200, new { success = true, message = "Message sent" });
                return;
            }

            if (path == "/api/v1/twitch/category")
            {
                var doc = await ReadJsonBodyAsync(context);
                LastReceivedGameId = doc.RootElement.GetProperty("gameId").GetString();
                WriteJson(context, 200, new { success = true, message = "Category updated", category = "Just Chatting" });
                return;
            }

            if (path == "/api/v1/twitch/title")
            {
                var doc = await ReadJsonBodyAsync(context);
                LastReceivedTitle = doc.RootElement.GetProperty("title").GetString();
                WriteJson(context, 200, new { success = true, message = "Title updated" });
                return;
            }

            if (path == "/api/v1/twitch/live-info")
            {
                WriteJson(context, 200, new
                {
                    success = true,
                    isLive = IsLive,
                    category = LiveCategory,
                    title = LiveTitle,
                    viewers = LiveViewers,
                    lastFollower = LastFollower
                });
                return;
            }

            if (path is "/api/v1/timer/pause" or "/api/v1/timer/resume" or "/api/v1/timer/stop")
            {
                ReceivedTimerCalls.Add(path);
                WriteJson(context, 200, new { success = true, message = "ok" });
                return;
            }

            if (path == "/api/v1/timer/start")
            {
                ReceivedTimerCalls.Add(path);
                var doc = await ReadJsonBodyAsync(context);
                LastReceivedDuration = doc.RootElement.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : null;
                WriteJson(context, 200, new { success = true, message = "Timer started" });
                return;
            }

            if (path == "/api/v1/timer/add")
            {
                ReceivedTimerCalls.Add(path);
                var doc = await ReadJsonBodyAsync(context);
                LastReceivedSeconds = doc.RootElement.GetProperty("seconds").GetInt32();
                WriteJson(context, 200, new { success = true, message = "Added", newTotalTime = 0 });
                return;
            }

            if (path == "/api/v1/sounds")
            {
                WriteJson(context, 200, new
                {
                    success = true,
                    sounds = Sounds.Select(s => new { id = s.Id, name = s.Name })
                });
                return;
            }

            if (path == "/api/v1/sounds/play")
            {
                var doc = await ReadJsonBodyAsync(context);
                LastReceivedSoundId = doc.RootElement.GetProperty("soundId").GetString();
                WriteJson(context, 200, new { success = true, message = "Sound alert triggered" });
                return;
            }

            if (path == "/api/v1/twitch/games/search")
            {
                WriteJson(context, 200, new
                {
                    success = true,
                    games = GameSearchResults.Select(g => new { id = g.Id, name = g.Name, box_art_url = (string?)null })
                });
                return;
            }

            WriteJson(context, 404, new { error = "not_found" });
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ya se habrá cerrado */ }
        }
    }

    private static async Task<JsonDocument> ReadJsonBodyAsync(HttpListenerContext context)
    {
        var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
        return JsonDocument.Parse(body);
    }

    private static void WriteJson(HttpListenerContext context, int statusCode, object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
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
