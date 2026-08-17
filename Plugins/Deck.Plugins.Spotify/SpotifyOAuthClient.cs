using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deck.Plugins.Spotify;

// Authorization Code + PKCE contra el endpoint de OAuth de Spotify — sin
// client secret embebido (PKCE es justamente el flujo pensado para apps
// distribuidas donde no hay dónde esconder un secreto). Primera integración
// real con OAuth del roadmap (Fase 4): valida que el Credential Manager de
// Fase 1 sirve para guardar un refresh_token, no solo una contraseña simple.
public class SpotifyOAuthClient
{
    private readonly HttpClient _http;
    private readonly string _authBaseUrl;

    public SpotifyOAuthClient(HttpClient http, string authBaseUrl = "https://accounts.spotify.com")
    {
        _http = http;
        _authBaseUrl = authBaseUrl.TrimEnd('/');
    }

    public static string GenerateCodeVerifier() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    public static string ComputeCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier)));

    public string BuildAuthorizationUrl(string clientId, string redirectUri, string scopes, string codeVerifier)
    {
        var challenge = ComputeCodeChallenge(codeVerifier);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["scope"] = scopes
        };

        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{_authBaseUrl}/authorize?{qs}";
    }

    public async Task<SpotifyTokens> ExchangeCodeAsync(
        string clientId, string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier
        };

        return await PostTokenRequestAsync(form, existingRefreshToken: null, ct);
    }

    public async Task<SpotifyTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId
        };

        return await PostTokenRequestAsync(form, existingRefreshToken: refreshToken, ct);
    }

    private async Task<SpotifyTokens> PostTokenRequestAsync(
        Dictionary<string, string> form, string? existingRefreshToken, CancellationToken ct)
    {
        using var response = await _http.PostAsync($"{_authBaseUrl}/api/token", new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new SpotifyAuthException($"Spotify rechazó la solicitud de token ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();

        // Spotify a veces no devuelve un refresh_token nuevo al refrescar —
        // en ese caso, el que ya teníamos sigue siendo válido.
        var refreshToken = root.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString()!
            : existingRefreshToken ?? throw new SpotifyAuthException("Spotify no devolvió refresh_token y no había uno previo.");

        return new SpotifyTokens(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
