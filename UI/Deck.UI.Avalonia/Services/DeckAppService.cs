using Deck.Core.Credentials;
using Deck.Core.Data;
using Deck.Core.Execution;
using Deck.Core.Model;
using Deck.Core.Plugins;
using Deck.Core.SystemActions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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

    private DeckAppService(DeckDbContext db, PluginManager plugins, ActionExecutor executor)
    {
        Db = db;
        Plugins = plugins;
        Executor = executor;
    }

    public static async Task<DeckAppService> StartAsync()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flowdeck");

        var db = new DeckDbContext(DeckDb.CreateOptions(Path.Combine(appDataDir, "flowdeck.db")));
        DeckDb.EnsureMigrated(db);

        var credentials = new SqliteCredentialManager(
            db, CredentialEncryptionKey.LoadOrCreate(Path.Combine(appDataDir, "credentials.key")));

        var plugins = new PluginManager(credentials, NullLoggerFactory.Instance);
        var systemPlugin = plugins.LoadInstance(new SystemActionsPlugin());
        await plugins.InitializeAsync(systemPlugin.Metadata.Id);
        await plugins.ConnectAsync(systemPlugin.Metadata.Id);

        await SeedIfEmptyAsync(db);

        return new DeckAppService(db, plugins, new ActionExecutor(plugins));
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
