using MobileDeck.Core;

namespace MobileDeck.Services;

public enum ConnectionPhase { Disconnected, Connecting, Connected, Failed }

// Orquesta DeckApiClient + DeckHubClient + DeckNavigationStack — mismo rol
// que App.tsx del lado de Web Deck, pero como servicio inyectable (singleton
// en MauiProgram.cs) para que cualquier página Razor pueda suscribirse a
// StateChanged en vez de tener toda la lógica metida en un solo componente.
public sealed class DeckConnectionService : IAsyncDisposable
{
    private readonly DeckNavigationStack _navigation = new();

    private DeckApiClient? _api;
    private DeckHubClient? _hub;

    public ConnectionPhase Phase { get; private set; } = ConnectionPhase.Disconnected;
    public string? ConnectError { get; private set; }
    public HubConnectionStatus HubStatus { get; private set; } = HubConnectionStatus.Disconnected;

    public ProfileDto? Profile { get; private set; }
    public IReadOnlyList<PluginDto> Plugins { get; private set; } = [];
    public PageDto? CurrentPage => _navigation.Current;
    public bool CanGoBack => _navigation.CanGoBack;

    public event Action? StateChanged;

    public async Task ConnectAsync(string serverUrl, string pairingKey)
    {
        Phase = ConnectionPhase.Connecting;
        ConnectError = null;
        StateChanged?.Invoke();

        try
        {
            var normalizedUrl = NormalizeServerUrl(serverUrl);

            _api = new DeckApiClient(new HttpClient { BaseAddress = new Uri(normalizedUrl) }, pairingKey);

            var profiles = await _api.GetProfilesAsync();
            var profile = profiles.FirstOrDefault()
                ?? throw new InvalidOperationException("El Core no tiene ningún perfil todavía.");

            var rootPage = await _api.GetPageAsync(profile.RootPageId);

            Profile = profile;
            _navigation.Reset(rootPage);

            _hub = new DeckHubClient(normalizedUrl, pairingKey, clientType: "MobileDeck");
            _hub.StatusChanged += status => { HubStatus = status; StateChanged?.Invoke(); };
            _hub.PluginEventReceived += pluginEvent => _ = RefreshPluginsAsync();

            await _hub.ConnectAsync();
            await _hub.SetActivePageAsync(profile.Id, rootPage.Id);
            await RefreshPluginsAsync();

            Phase = ConnectionPhase.Connected;
        }
        catch (DeckApiException ex) when (ex.IsAuthError)
        {
            Phase = ConnectionPhase.Failed;
            ConnectError = "Pairing key incorrecta.";
            await DisconnectInternalAsync();
        }
        catch (Exception)
        {
            Phase = ConnectionPhase.Failed;
            ConnectError = "No se pudo conectar. Revisá que Deck.Api esté corriendo en esa dirección.";
            await DisconnectInternalAsync();
        }

        StateChanged?.Invoke();
    }

    public async Task<bool> PressButtonAsync(int row, int column)
    {
        if (_hub is null || CurrentPage is null) return false;

        var result = await _hub.ExecuteButtonAsync(CurrentPage.Id, row, column);

        if (result.NavigatedToPageId is { } targetPageId && _api is not null)
        {
            var nextPage = await _api.GetPageAsync(targetPageId);
            _navigation.Push(nextPage);
            if (Profile is not null) await _hub.SetActivePageAsync(Profile.Id, nextPage.Id);
            StateChanged?.Invoke();
            return true;
        }

        return result.Success;
    }

    public async Task GoBackAsync()
    {
        if (!CanGoBack) return;
        _navigation.Pop();

        if (_hub is not null && Profile is not null && CurrentPage is not null)
        {
            await _hub.SetActivePageAsync(Profile.Id, CurrentPage.Id);
        }

        StateChanged?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        await DisconnectInternalAsync();
        Phase = ConnectionPhase.Disconnected;
        StateChanged?.Invoke();
    }

    private async Task RefreshPluginsAsync()
    {
        // Plugins se leen por REST, no por hub — no vale la pena duplicar el
        // estado de plugins sobre SignalR, alcanza con refrescar al conectar
        // y cada vez que llega un PluginEvent (cambio de conexión, etc).
        if (_api is null) return;

        try
        {
            Plugins = await _api.GetPluginsAsync();
            StateChanged?.Invoke();
        }
        catch
        {
            // no bloquea la conexión si falla un refresh de plugins puntual
        }
    }

    private async Task DisconnectInternalAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        _hub = null;
        _api = null;
        Profile = null;
        Plugins = [];
        HubStatus = HubConnectionStatus.Disconnected;
    }

    private static string NormalizeServerUrl(string input)
    {
        var trimmed = input.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"http://{trimmed}";
    }

    public async ValueTask DisposeAsync() => await DisconnectInternalAsync();
}
