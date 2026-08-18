using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deck.Core.Credentials;

namespace Deck.Core.Auth;

// Login "Conectar con Decatron" — Authorization Code + PKCE contra el
// servidor OAuth2 que el bot de Decatron ya tiene en producción
// (twitch.decatron.net/api/oauth). No hay client_secret embebido: el repo
// de Flowdeck es público, y el endpoint de refresh de ese backend exige
// client_secret siempre (aunque se haya usado PKCE), así que no se guarda
// ningún refresh_token — cuando el access token vence (1h, fijo del lado
// del bot), hay que volver a llamar LoginAsync. Como ya queda sesión
// abierta en decatron.net, reautorizar es un solo click de "Aprobar", no
// un login completo de nuevo. Ver plan en el panel admin de Flowdeck,
// content/docs/02-integracion-bot-decatron.
public sealed class DecatronAuthService
{
    public const string PluginId = "decatron";

    private const string AuthorizeUrl = "https://decatron.net/oauth/authorize";
    private const string TokenUrl = "https://twitch.decatron.net/api/oauth/token";
    private const string UserInfoUrl = "https://twitch.decatron.net/api/oauth/userinfo";
    private const string RevokeUrl = "https://twitch.decatron.net/api/oauth/revoke";
    // action:chat sumado para que Flowdeck pueda mandar mensajes al chat de
    // Twitch usando este mismo login (vía DecatronPlugin) — sin este scope
    // en el token, twitch.decatron.net/api/v1/chat/send devuelve 403. Quien
    // ya estaba conectado de antes necesita reconectar una vez para que el
    // token nuevo incluya el scope.
    private const string Scope = "read:profile action:chat";

    // Tiene que matchear EXACTO (con la barra final) lo que se registre
    // como redirect URI en el Developer Portal del bot — el backend valida
    // coincidencia exacta, no por prefijo.
    public const string RedirectUri = "http://127.0.0.1:51823/callback/";

    private readonly ICredentialManager _credentials;
    private readonly string _clientId;
    private readonly HttpClient _http;

    public DecatronAuthService(ICredentialManager credentials, string clientId, HttpClient? http = null)
    {
        _credentials = credentials;
        _clientId = clientId;
        _http = http ?? new HttpClient();
    }

    public static string GenerateCodeVerifier() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    public static string ComputeCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier)));

    public async Task<DecatronAccount> LoginAsync(CancellationToken ct = default)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        try
        {
            Process.Start(new ProcessStartInfo(BuildAuthorizeUrl(state, codeChallenge)) { UseShellExecute = true });

            var (code, receivedState, error) = await WaitForCallbackAsync(listener, ct);

            if (error is not null)
                throw new InvalidOperationException($"Decatron rechazó la autorización: {error}");
            if (receivedState != state || code is null)
                throw new InvalidOperationException("Respuesta de Decatron inválida (state no coincide o falta el code).");

            var (accessToken, expiresAt) = await ExchangeCodeAsync(code, codeVerifier, ct);
            var displayName = await FetchDisplayNameAsync(accessToken, ct);

            await _credentials.SetAsync(PluginId, "access-token", accessToken, ct);
            await _credentials.SetAsync(PluginId, "expires-at", expiresAt.ToString("O"), ct);
            await _credentials.SetAsync(PluginId, "display-name", displayName, ct);

            return new DecatronAccount(displayName, expiresAt);
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var token = await _credentials.GetAsync(PluginId, "access-token", ct);
        if (token is not null)
        {
            try
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
                await _http.PostAsync(RevokeUrl, form, ct);
            }
            catch
            {
                // Si el revoke remoto falla (sin red, backend caído), igual
                // se borra la credencial local — no queremos que el usuario
                // quede "atascado conectado" en Flowdeck por eso.
            }
        }

        await _credentials.DeleteAsync(PluginId, "access-token", ct);
        await _credentials.DeleteAsync(PluginId, "expires-at", ct);
        await _credentials.DeleteAsync(PluginId, "display-name", ct);
    }

    public async Task<DecatronAccount?> GetStatusAsync(CancellationToken ct = default)
    {
        var token = await _credentials.GetAsync(PluginId, "access-token", ct);
        var expiresAtRaw = await _credentials.GetAsync(PluginId, "expires-at", ct);
        var displayName = await _credentials.GetAsync(PluginId, "display-name", ct);

        if (token is null || expiresAtRaw is null) return null;
        if (!DateTimeOffset.TryParse(expiresAtRaw, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow)
            return null;

        return new DecatronAccount(displayName ?? "Decatron", expiresAt);
    }

    private string BuildAuthorizeUrl(string state, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{AuthorizeUrl}?{qs}";
    }

    private static async Task<(string? Code, string? State, string? Error)> WaitForCallbackAsync(
        HttpListener listener, CancellationToken ct)
    {
        var contextTask = listener.GetContextAsync();
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(3), ct);
        var completed = await Task.WhenAny(contextTask, timeoutTask);

        if (completed != contextTask)
            throw new TimeoutException("No llegó respuesta de Decatron a tiempo — se canceló el login.");

        var context = await contextTask;
        var code = context.Request.QueryString["code"];
        var state = context.Request.QueryString["state"];
        var error = context.Request.QueryString["error"];

        await RespondAsync(context, success: error is null);
        return (code, state, error);
    }

    // Mismos tokens de color que FlowdeckTheme.axaml (la app) y
    // flowdeck.decatron.net — esta pestaña vive dos segundos, pero es lo
    // último que ve el usuario del lado del navegador antes de volver a la
    // app, así que tiene que sentirse igual de cuidada. Intenta cerrarse
    // sola (funciona si el navegador la trata como pestaña "de script"; si
    // no, el texto ya le dice al usuario que la cierre a mano).
    private static async Task RespondAsync(HttpListenerContext context, bool success)
    {
        var (icon, heading, subtext) = success
            ? ("""<path d="M7 13l3 3 7-7" stroke="#2563EB" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>""",
               "Listo.",
               "Ya podés volver a Flowdeck — esta pestaña se cierra sola.")
            : ("""<path d="M8 8l8 8M16 8l-8 8" stroke="#EF4444" stroke-width="2.2" stroke-linecap="round"/>""",
               "Algo falló.",
               "Volvé a Flowdeck e intentá conectar de nuevo.");

        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8" />
            <title>Flowdeck</title>
            <style>
              :root { color-scheme: dark; }
              * { box-sizing: border-box; }
              body {
                margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                background: #14171C; color: #E7EAEE;
                font-family: -apple-system, "Segoe UI", Inter, Roboto, sans-serif;
              }
              .card {
                width: 320px; padding: 28px 32px; text-align: center;
                background: #1C2028; border: 1px solid #2C323D; border-radius: 12px;
              }
              .brand { display: flex; align-items: center; justify-content: center; gap: 8px; margin-bottom: 22px; }
              .dot { width: 8px; height: 8px; border-radius: 50%; background: #2563EB; }
              .wordmark { font-size: 13px; font-weight: 600; letter-spacing: 0.01em; color: #E7EAEE; }
              .icon-ring {
                width: 44px; height: 44px; margin: 0 auto 16px; border-radius: 50%;
                display: flex; align-items: center; justify-content: center;
                background: rgba(37, 99, 235, 0.12);
              }
              h1 { font-size: 17px; font-weight: 600; margin: 0 0 6px; }
              p { font-size: 13px; line-height: 1.5; color: #8A94A6; margin: 0; }
            </style>
            </head>
            <body>
              <div class="card">
                <div class="brand"><span class="dot"></span><span class="wordmark">Flowdeck</span></div>
                <div class="icon-ring"><svg width="24" height="24" viewBox="0 0 24 24">{{icon}}</svg></div>
                <h1>{{heading}}</h1>
                <p>{{subtext}}</p>
              </div>
              <script>setTimeout(function () { window.close(); }, 900);</script>
            </body>
            </html>
            """;

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }

    private async Task<(string AccessToken, DateTimeOffset ExpiresAt)> ExchangeCodeAsync(
        string code, string codeVerifier, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = _clientId,
            ["code_verifier"] = codeVerifier
        };

        using var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Decatron rechazó el canje de token ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();

        return (accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private async Task<string> FetchDisplayNameAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"No se pudo leer el perfil de Decatron ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var displayName = root.TryGetProperty("display_name", out var dn) ? dn.GetString() : null;
        var login = root.TryGetProperty("login", out var lg) ? lg.GetString() : null;

        return displayName ?? login ?? "Decatron";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record DecatronAccount(string DisplayName, DateTimeOffset ExpiresAt);
