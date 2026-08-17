using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MobileDeck.Core;

// Cliente REST contra Deck.Api — solo lo que la app necesita para arrancar y
// navegar (perfiles, páginas). La ejecución en tiempo real va por
// DeckHubClient (SignalR), no por acá.
public sealed class DeckApiClient
{
    // Mismo criterio que Program.cs del lado del servidor: enums como texto.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public DeckApiClient(HttpClient http, string pairingKey)
    {
        _http = http;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pairingKey);
    }

    // Sin pairing key a propósito — permite distinguir "dirección equivocada"
    // (esto nunca responde) de "pairing key equivocada" (esto sí, pero
    // /api/profiles da 401) desde la pantalla de conexión.
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ProfileDto>>("/api/profiles", ct);

    public Task<PageDto> GetPageAsync(Guid pageId, CancellationToken ct = default) =>
        GetAsync<PageDto>($"/api/pages/{pageId}", ct);

    public Task<IReadOnlyList<PluginDto>> GetPluginsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<PluginDto>>("/api/plugins", ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new DeckApiException((int)response.StatusCode, $"{(int)response.StatusCode} {response.ReasonPhrase} — {path}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }
}
