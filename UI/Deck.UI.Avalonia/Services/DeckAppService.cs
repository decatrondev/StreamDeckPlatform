using Deck.Core.Credentials;
using Deck.Core.Data;
using Deck.Core.Execution;
using Deck.Core.Icons;
using Deck.Core.Model;
using Deck.Core.Plugins;
using Deck.Core.SystemActions;
using Deck.Plugins.Discord;
using Deck.Plugins.Obs;
using Deck.Plugins.Spotify;
using Deck.Plugins.Twitch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Deck.UI.Avalonia.Services;

// Punto único de arranque del Core para el Virtual Deck: base local (SQLite en
// AppData, la misma en Windows/Linux/macOS gracias a SpecialFolder.ApplicationData),
// plugin de acciones de sistema registrado, datos semilla si es la primera vez
// que se abre la app.
public class DeckAppService
{
    public DeckDbContext Db { get; }
    public PluginManager Plugins { get; }
    public ActionExecutor Executor { get; }
    public ICredentialManager Credentials { get; }
    public IconStore Icons { get; }

    private DeckAppService(
        DeckDbContext db, PluginManager plugins, ActionExecutor executor, ICredentialManager credentials, IconStore icons)
    {
        Db = db;
        Plugins = plugins;
        Executor = executor;
        Credentials = credentials;
        Icons = icons;
    }

    public static async Task<DeckAppService> StartAsync()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flowdeck");

        var db = new DeckDbContext(DeckDb.CreateOptions(Path.Combine(appDataDir, "flowdeck.db")));
        DeckDb.EnsureMigrated(db);

        var credentials = new SqliteCredentialManager(
            db, CredentialEncryptionKey.LoadOrCreate(Path.Combine(appDataDir, "credentials.key")));
        var icons = new IconStore(appDataDir);

        // Sin esto no había forma de que el usuario final nos pase un error real —
        // solo veía "no conecta" sin ningún detalle. Un archivo por día, 14 días de
        // retención, en la misma carpeta de AppData que la db y las credenciales.
        var logsDir = Path.Combine(appDataDir, "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logsDir, "flowdeck-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: true));
        loggerFactory.CreateLogger("Flowdeck.Startup").LogInformation("Flowdeck arrancando. AppData: {AppDataDir}", appDataDir);

        var plugins = new PluginManager(credentials, loggerFactory);

        // OBS apunta al puerto default de obs-websocket (ws://127.0.0.1:4455) —
        // la contraseña, si el usuario tiene una configurada en OBS, se carga
        // sola del Credential Manager (ver PluginSettingsView). Spotify/Discord/
        // Twitch ya arrancan con Client ID real (ver PluginClientIds) — Discord
        // puede conectar de una (RPC local, sin redirect URI). Twitch y Spotify
        // todavía no van a poder loguear un usuario hasta que se sume el
        // listener local de OAuth y se registre su redirect URI en cada
        // dashboard — cargarlos ya mismo no rompe nada, cada uno maneja "sin
        // conectar todavía" sin tirar excepción (ver Fases 3-6).
        var loadedPlugins = new[]
        {
            plugins.LoadInstance(new SystemActionsPlugin()),
            plugins.LoadInstance(new ObsPlugin()),
            plugins.LoadInstance(new SpotifyPlugin(PluginClientIds.Spotify)),
            plugins.LoadInstance(new DiscordPlugin(PluginClientIds.Discord)),
            plugins.LoadInstance(new TwitchPlugin(PluginClientIds.Twitch)),
        };

        foreach (var plugin in loadedPlugins)
        {
            await plugins.InitializeAsync(plugin.Metadata.Id);
            await plugins.ConnectAsync(plugin.Metadata.Id);
        }

        await SeedIfEmptyAsync(db);

        return new DeckAppService(db, plugins, new ActionExecutor(plugins), credentials, icons);
    }

    private static async Task SeedIfEmptyAsync(DeckDbContext db)
    {
        if (await db.Profiles.AnyAsync()) return;

        var page = new Page { Id = Guid.NewGuid(), Name = "Principal", Rows = 3, Columns = 5 };
        var profile = new Profile { Id = Guid.NewGuid(), Name = "Principal", RootPageId = page.Id };

        db.Pages.Add(page);
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
    }
}
