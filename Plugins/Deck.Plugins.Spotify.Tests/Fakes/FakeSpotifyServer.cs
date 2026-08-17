using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deck.Plugins.Spotify.Tests.Fakes;

// Sirve tanto de servidor de autorización (/api/token) como de Web API
// (/v1/me/player/*) — no hace falta separarlos, es un solo HttpListener con
// rutas distintas. Suficiente para probar el flujo completo de OAuth+PKCE y
// las acciones de reproducción sin pegarle a Spotify real.
public sealed class FakeSpotifyServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentBag<Task> _handlers = [];
    private readonly ConcurrentQueue<(string Method, string Path, string? Query)> _receivedApiCalls = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public Uri BaseUrl => new($"http://127.0.0.1:{Port}");

    public string ExpectedAuthCode { get; set; } = "test-auth-code";
    public string? ExpectedCodeChallenge { get; set; }
    public string ValidRefreshToken { get; set; } = "initial-refresh-token";
    public bool RejectRefresh { get; set; }
    public bool RotateRefreshTokenOnRefresh { get; set; }
    public bool RejectNextApiCall { get; set; } // simula un access_token vencido a mitad de sesión

    public string? CurrentlyPlayingTrackId { get; set; } = "track-1";
    public string CurrentlyPlayingTrackName { get; set; } = "Canción de prueba";
    public string CurrentlyPlayingArtist { get; set; } = "Artista de prueba";
    public bool CurrentlyPlayingIsPlaying { get; set; } = true;

    public IReadOnlyCollection<(string Method, string Path, string? Query)> ReceivedApiCalls => _receivedApiCalls.ToArray();

    private string _currentAccessToken = "";

    public FakeSpotifyServer()
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
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch
            {
                return;
            }

            _handlers.Add(Task.Run(() => HandleAsync(context), ct));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url!.AbsolutePath;

            if (path == "/api/token" && context.Request.HttpMethod == "POST")
            {
                await HandleTokenRequestAsync(context);
            }
            else if (path == "/v1/me/player/currently-playing" && context.Request.HttpMethod == "GET")
            {
                HandleCurrentlyPlaying(context);
            }
            else if (path.StartsWith("/v1/me/player/"))
            {
                HandlePlayerAction(context);
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ya se habrá cerrado */ }
        }
    }

    private async Task HandleTokenRequestAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync();
        var form = ParseFormUrlEncoded(body);
        form.TryGetValue("grant_type", out var grantType);

        string? refreshTokenToReturn = null;

        if (grantType == "authorization_code")
        {
            form.TryGetValue("code", out var code);
            form.TryGetValue("code_verifier", out var verifier);

            if (code != ExpectedAuthCode)
            {
                WriteJson(context, 400, new { error = "invalid_grant", error_description = "code no coincide" });
                return;
            }

            if (ExpectedCodeChallenge is not null && ComputeChallenge(verifier ?? "") != ExpectedCodeChallenge)
            {
                WriteJson(context, 400, new { error = "invalid_grant", error_description = "PKCE challenge no coincide" });
                return;
            }

            refreshTokenToReturn = ValidRefreshToken;
        }
        else if (grantType == "refresh_token")
        {
            form.TryGetValue("refresh_token", out var refreshToken);

            if (RejectRefresh || refreshToken != ValidRefreshToken)
            {
                WriteJson(context, 400, new { error = "invalid_grant", error_description = "refresh_token inválido" });
                return;
            }

            if (RotateRefreshTokenOnRefresh)
            {
                ValidRefreshToken = $"rotated-{Guid.NewGuid():N}";
                refreshTokenToReturn = ValidRefreshToken;
            }
        }
        else
        {
            WriteJson(context, 400, new { error = "unsupported_grant_type" });
            return;
        }

        _currentAccessToken = $"access-{Guid.NewGuid():N}";

        WriteJson(context, 200, refreshTokenToReturn is null
            ? new { access_token = _currentAccessToken, token_type = "Bearer", expires_in = 3600, scope = "" }
            : new { access_token = _currentAccessToken, token_type = "Bearer", expires_in = 3600, scope = "", refresh_token = refreshTokenToReturn });
    }

    private void HandleCurrentlyPlaying(HttpListenerContext context)
    {
        if (!AuthorizeOrReject(context)) return;

        if (CurrentlyPlayingTrackId is null)
        {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        WriteJson(context, 200, new
        {
            is_playing = CurrentlyPlayingIsPlaying,
            item = new
            {
                id = CurrentlyPlayingTrackId,
                name = CurrentlyPlayingTrackName,
                artists = new[] { new { name = CurrentlyPlayingArtist } }
            }
        });
    }

    private void HandlePlayerAction(HttpListenerContext context)
    {
        if (!AuthorizeOrReject(context)) return;

        _receivedApiCalls.Enqueue((context.Request.HttpMethod, context.Request.Url!.AbsolutePath, context.Request.Url.Query));

        context.Response.StatusCode = 204;
        context.Response.Close();
    }

    private bool AuthorizeOrReject(HttpListenerContext context)
    {
        if (RejectNextApiCall)
        {
            RejectNextApiCall = false;
            context.Response.StatusCode = 401;
            context.Response.Close();
            return false;
        }

        var authHeader = context.Request.Headers["Authorization"];
        if (authHeader != $"Bearer {_currentAccessToken}")
        {
            context.Response.StatusCode = 401;
            context.Response.Close();
            return false;
        }

        return true;
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

    private static string ComputeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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
