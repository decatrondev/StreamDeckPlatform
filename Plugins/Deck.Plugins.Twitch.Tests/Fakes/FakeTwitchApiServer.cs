using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Deck.Plugins.Twitch.Tests.Fakes;

// Sirve /oauth2/token (auth) y /helix/* (API) en el mismo HttpListener — igual
// que el servidor falso de Spotify, la única diferencia real de Twitch es que
// cada request de Helix además exige el header Client-Id.
public sealed class FakeTwitchApiServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentBag<Task> _handlers = [];
    private readonly ConcurrentQueue<(string Method, string Path, string Body)> _receivedApiCalls = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public Uri BaseUrl => new($"http://127.0.0.1:{Port}");

    public string ExpectedClientId { get; set; } = "test-client-id";
    public string ExpectedAuthCode { get; set; } = "test-auth-code";
    public string? ExpectedCodeChallenge { get; set; }
    public string ValidRefreshToken { get; set; } = "initial-refresh-token";
    public bool RejectRefresh { get; set; }
    public bool RejectNextApiCall { get; set; }
    public string BroadcasterUserId { get; set; } = "broadcaster-1";
    public List<(string Id, string Name)> CategorySearchResults { get; set; } = [];

    public IReadOnlyCollection<(string Method, string Path, string Body)> ReceivedApiCalls => _receivedApiCalls.ToArray();

    private string _currentAccessToken = "";

    public FakeTwitchApiServer()
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
        try { await Task.WhenAll(_handlers).WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* timeout, no bloquea */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(ct); }
            catch { return; }

            _handlers.Add(Task.Run(() => HandleAsync(context), ct));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url!.AbsolutePath;

            if (path == "/oauth2/token" && context.Request.HttpMethod == "POST")
            {
                await HandleTokenRequestAsync(context);
                return;
            }

            if (path == "/helix/users")
            {
                HandleAuthorizedRequest(context, () => WriteJson(context, 200, new
                {
                    data = new[] { new { id = BroadcasterUserId, login = "broadcaster", display_name = "Broadcaster" } }
                }));
                return;
            }

            if (path == "/helix/search/categories" && context.Request.HttpMethod == "GET")
            {
                HandleAuthorizedRequest(context, () => WriteJson(context, 200, new
                {
                    data = CategorySearchResults.Select(c => new { id = c.Id, name = c.Name }).ToArray()
                }));
                return;
            }

            if (path.StartsWith("/helix/"))
            {
                var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
                HandleAuthorizedRequest(context, () =>
                {
                    _receivedApiCalls.Enqueue((context.Request.HttpMethod, path + context.Request.Url.Query, body));
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                });
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ya se habrá cerrado */ }
        }
    }

    private void HandleAuthorizedRequest(HttpListenerContext context, Action onAuthorized)
    {
        if (context.Request.Headers["Client-Id"] != ExpectedClientId)
        {
            context.Response.StatusCode = 401;
            context.Response.Close();
            return;
        }

        if (RejectNextApiCall)
        {
            RejectNextApiCall = false;
            context.Response.StatusCode = 401;
            context.Response.Close();
            return;
        }

        var authHeader = context.Request.Headers["Authorization"];
        if (authHeader != $"Bearer {_currentAccessToken}")
        {
            context.Response.StatusCode = 401;
            context.Response.Close();
            return;
        }

        onAuthorized();
    }

    private async Task HandleTokenRequestAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync();
        var form = ParseFormUrlEncoded(body);
        form.TryGetValue("grant_type", out var grantType);

        string? refreshTokenToReturn;

        if (grantType == "authorization_code")
        {
            form.TryGetValue("code", out var code);
            form.TryGetValue("code_verifier", out var verifier);

            if (code != ExpectedAuthCode)
            {
                WriteJson(context, 400, new { message = "code no coincide" });
                return;
            }

            if (ExpectedCodeChallenge is not null && ComputeChallenge(verifier ?? "") != ExpectedCodeChallenge)
            {
                WriteJson(context, 400, new { message = "PKCE challenge no coincide" });
                return;
            }

            refreshTokenToReturn = ValidRefreshToken;
        }
        else if (grantType == "refresh_token")
        {
            form.TryGetValue("refresh_token", out var refreshToken);

            if (RejectRefresh || refreshToken != ValidRefreshToken)
            {
                WriteJson(context, 400, new { message = "refresh_token inválido" });
                return;
            }

            refreshTokenToReturn = ValidRefreshToken;
        }
        else
        {
            WriteJson(context, 400, new { message = "grant_type no soportado" });
            return;
        }

        _currentAccessToken = $"access-{Guid.NewGuid():N}";

        WriteJson(context, 200, new
        {
            access_token = _currentAccessToken,
            refresh_token = refreshTokenToReturn,
            expires_in = 3600,
            token_type = "bearer",
            scope = Array.Empty<string>()
        });
    }

    private static string ComputeChallenge(string verifier)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static Dictionary<string, string> ParseFormUrlEncoded(string body)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            result[key] = value;
        }
        return result;
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
        await StopAsync();
        _listener.Close();
    }
}
