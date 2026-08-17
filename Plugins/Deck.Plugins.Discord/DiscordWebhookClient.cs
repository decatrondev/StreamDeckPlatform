using System.Net.Http.Json;

namespace Deck.Plugins.Discord;

// RPC no puede mandar mensajes de texto a un canal (eso queda fuera de su
// alcance, ver README de la Fase 5) — "enviar mensaje rápido" se resuelve con
// un webhook de canal, guardado vía Credential Manager como cualquier otro
// secreto de plugin.
public class DiscordWebhookClient
{
    private readonly HttpClient _http;

    public DiscordWebhookClient(HttpClient http)
    {
        _http = http;
    }

    public async Task SendMessageAsync(string webhookUrl, string content, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(webhookUrl, new { content }, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new DiscordWebhookException((int)response.StatusCode, body);
        }
    }
}
