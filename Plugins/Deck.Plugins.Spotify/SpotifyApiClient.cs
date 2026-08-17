using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Deck.Plugins.Spotify;

public sealed record CurrentlyPlaying(string? TrackId, string? TrackName, string? ArtistName, bool IsPlaying);

// 401 puntual: token vencido a mitad de sesión — el llamador decide si
// refresca y reintenta. No es un error de red genérico.
public class SpotifyUnauthorizedException : Exception
{
}

public class SpotifyApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiBaseUrl;

    public SpotifyApiClient(HttpClient http, string apiBaseUrl = "https://api.spotify.com")
    {
        _http = http;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
    }

    public Task PlayAsync(string accessToken, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "/v1/me/player/play", accessToken, ct);

    public Task PauseAsync(string accessToken, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "/v1/me/player/pause", accessToken, ct);

    public Task NextAsync(string accessToken, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/v1/me/player/next", accessToken, ct);

    public Task PreviousAsync(string accessToken, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/v1/me/player/previous", accessToken, ct);

    public Task SetVolumeAsync(string accessToken, int volumePercent, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"/v1/me/player/volume?volume_percent={volumePercent}", accessToken, ct);

    public async Task<CurrentlyPlaying?> GetCurrentlyPlayingAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBaseUrl}/v1/me/player/currently-playing");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new SpotifyUnauthorizedException();
        if (response.StatusCode == HttpStatusCode.NoContent) return null; // nada sonando

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return null;

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null) return null;

        var artist = item.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0
            ? artists[0].GetProperty("name").GetString()
            : null;

        return new CurrentlyPlaying(
            TrackId: item.GetProperty("id").GetString(),
            TrackName: item.GetProperty("name").GetString(),
            ArtistName: artist,
            IsPlaying: root.TryGetProperty("is_playing", out var ip) && ip.GetBoolean());
    }

    private async Task SendAsync(HttpMethod method, string pathAndQuery, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{_apiBaseUrl}{pathAndQuery}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new SpotifyUnauthorizedException();

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SpotifyApiException((int)response.StatusCode, body);
        }
    }
}
