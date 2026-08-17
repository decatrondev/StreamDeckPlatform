using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deck.Plugins.Discord;
using Deck.Plugins.Obs;
using Deck.Plugins.Spotify;
using Deck.Plugins.Twitch;
using Deck.UI.Avalonia.Services;

namespace Deck.UI.Avalonia.ViewModels;

// Sin esto, la contraseña de OBS (si el usuario tiene una configurada, que es
// el default desde OBS 28) no tenía ninguna forma de llegar a la app —
// "cablear el plugin" en Fase 9 lo dejaba cargado pero nunca conectable.
// "Todo flexible" (filosofía del proyecto): nada hardcodeado, el usuario
// final carga sus propios datos acá.
public partial class SettingsDialogViewModel : ViewModelBase
{
    private readonly DeckAppService _app;

    [ObservableProperty]
    public partial string ObsPassword { get; set; } = "";

    [ObservableProperty]
    public partial string ObsStatus { get; set; } = "";

    [ObservableProperty]
    public partial string DiscordStatus { get; set; } = "";

    [ObservableProperty]
    public partial string TwitchStatus { get; set; } = "";

    [ObservableProperty]
    public partial string SpotifyStatus { get; set; } = "";

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    public IAsyncRelayCommand SaveObsCommand { get; }
    public IRelayCommand CloseCommand { get; }

    public event Action? Closed;

    public SettingsDialogViewModel() : this(null!) { }

    public SettingsDialogViewModel(DeckAppService app)
    {
        _app = app;
        SaveObsCommand = new AsyncRelayCommand(SaveObsAsync);
        CloseCommand = new RelayCommand(() => Closed?.Invoke());
        RefreshStatuses();
    }

    private void RefreshStatuses()
    {
        if (_app is null) return;

        ObsStatus = Describe(GetInstance<ObsPlugin>(ObsPlugin.PluginId)?.ConnectionState.ToString());
        DiscordStatus = Describe(GetInstance<DiscordPlugin>(DiscordPlugin.PluginId)?.ConnectionState.ToString());
        TwitchStatus = Describe(GetInstance<TwitchPlugin>(TwitchPlugin.PluginId)?.ConnectionState.ToString());
        SpotifyStatus = Describe(GetInstance<SpotifyPlugin>(SpotifyPlugin.PluginId)?.ConnectionState.ToString());
    }

    private static string Describe(string? state) => state switch
    {
        null => "no cargado",
        "Connected" => "conectado",
        "Connecting" => "conectando…",
        "Reconnecting" => "reconectando…",
        "AuthenticationFailed" => "falló la autenticación — revisá la contraseña",
        "NotAuthorized" => "sin autorizar todavía",
        _ => "desconectado",
    };

    private T? GetInstance<T>(string pluginId) where T : class =>
        _app.Plugins.Plugins.FirstOrDefault(p => p.Metadata.Id == pluginId)?.Instance as T;

    private async Task SaveObsAsync()
    {
        FeedbackMessage = "Guardando…";

        if (!string.IsNullOrWhiteSpace(ObsPassword))
        {
            await _app.Credentials.SetAsync(ObsPlugin.PluginId, "password", ObsPassword);
        }

        await _app.Plugins.DisconnectAsync(ObsPlugin.PluginId);
        await _app.Plugins.ConnectAsync(ObsPlugin.PluginId);

        // El handshake de obs-websocket no es instantáneo — sin esta espera
        // el status leído acá siempre muestra "conectando…" aunque haya
        // funcionado bien.
        await Task.Delay(800);
        RefreshStatuses();

        FeedbackMessage = ObsStatus == "conectado"
            ? "OBS conectado."
            : $"OBS: {ObsStatus}";
    }
}
