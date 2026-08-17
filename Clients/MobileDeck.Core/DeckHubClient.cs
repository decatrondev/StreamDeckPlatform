using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MobileDeck.Core;

public enum HubConnectionStatus { Connecting, Connected, Reconnecting, Disconnected }

// Wrapper sobre HubConnection — mismo rol que lib/hub.ts en Web Deck, pero
// del lado .NET el AccessTokenProvider alcanza para todo (a diferencia del
// browser, el cliente de SignalR acá SÍ puede mandar el header Authorization
// en el propio handshake de WebSocket, no hace falta el query string
// ?access_token= que usa la versión JS).
public sealed class DeckHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public event Action<HubConnectionStatus>? StatusChanged;
    public event Action<PluginEventMessage>? PluginEventReceived;

    // configureHttpOptions es un gancho de testing: los tests corren contra un
    // WebApplicationFactory en memoria y necesitan enchufar su propio
    // HttpMessageHandler ahí (ver MobileDeck.Core.Tests) — en producción se
    // deja null y SignalR arma la conexión real como siempre.
    public DeckHubClient(
        string baseUrl, string pairingKey, string clientType = "MobileDeck",
        Action<HttpConnectionOptions>? configureHttpOptions = null)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');

        _connection = new HubConnectionBuilder()
            .WithUrl($"{normalizedBaseUrl}/hubs/deck?clientType={clientType}", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(pairingKey);
                configureHttpOptions?.Invoke(options);
            })
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)])
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        _connection.On<PluginEventMessage>("PluginEvent", e => PluginEventReceived?.Invoke(e));
        _connection.Reconnecting += _ => { StatusChanged?.Invoke(HubConnectionStatus.Reconnecting); return Task.CompletedTask; };
        _connection.Reconnected += _ => { StatusChanged?.Invoke(HubConnectionStatus.Connected); return Task.CompletedTask; };
        _connection.Closed += _ => { StatusChanged?.Invoke(HubConnectionStatus.Disconnected); return Task.CompletedTask; };
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        StatusChanged?.Invoke(HubConnectionStatus.Connecting);
        await _connection.StartAsync(ct);
        StatusChanged?.Invoke(HubConnectionStatus.Connected);
    }

    public Task<ExecuteButtonResult> ExecuteButtonAsync(Guid pageId, int row, int column, CancellationToken ct = default) =>
        _connection.InvokeAsync<ExecuteButtonResult>("ExecuteButton", pageId, row, column, ct);

    public Task SetActivePageAsync(Guid profileId, Guid pageId, CancellationToken ct = default) =>
        _connection.SendAsync("SetActivePage", profileId, pageId, ct);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
