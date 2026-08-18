using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;

namespace Deck.Plugins.Twitch;

// Helix (la REST API "nueva" de Twitch) — todo pasa por acá salvo la conexión
// de EventSub en sí (esa es un WebSocket aparte, ver TwitchEventSubClient).
// Toda request de Helix necesita el Bearer del usuario Y el Client-Id de la
// app — a diferencia de OBS/Spotify/Discord, acá van los dos siempre juntos.
public class TwitchApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _apiBaseUrl;

    public TwitchApiClient(HttpClient http, string clientId, string apiBaseUrl = "https://api.twitch.tv")
    {
        _http = http;
        _clientId = clientId;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
    }

    public async Task<string> GetUserIdAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "/helix/users", accessToken, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
    }

    public async Task<IReadOnlyList<(string Id, string Name)>> SearchCategoriesAsync(string accessToken, string query, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/helix/search/categories?query={Uri.EscapeDataString(query)}", accessToken, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(c => (c.GetProperty("id").GetString()!, c.GetProperty("name").GetString()!))
            .ToList();
    }

    public Task ModifyChannelAsync(string accessToken, string broadcasterId, string? title, string? categoryId, CancellationToken ct)
    {
        var body = new Dictionary<string, string>();
        if (title is not null) body["title"] = title;
        if (categoryId is not null) body["game_id"] = categoryId;

        return SendAsync(HttpMethod.Patch, $"/helix/channels?broadcaster_id={broadcasterId}", accessToken, ct, body);
    }

    public Task CreateStreamMarkerAsync(string accessToken, string userId, string? description, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/helix/streams/markers", accessToken, ct,
            new { user_id = userId, description });

    public Task SendChatMessageAsync(string accessToken, string broadcasterId, string senderId, string message, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/helix/chat/messages", accessToken, ct,
            new { broadcaster_id = broadcasterId, sender_id = senderId, message });

    public Task CreateEventSubSubscriptionAsync(
        string accessToken, string type, string version, object condition, string sessionId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/helix/eventsub/subscriptions", accessToken, ct, new
        {
            type,
            version,
            condition,
            transport = new { method = "websocket", session_id = sessionId }
        });

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string pathAndQuery, string accessToken, CancellationToken ct, object? body = null)
    {
        using var request = new HttpRequestMessage(method, $"{_apiBaseUrl}{pathAndQuery}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", _clientId);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new TwitchUnauthorizedException();

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            throw new TwitchApiException((int)response.StatusCode, responseBody);
        }

        return response;
    }
}
