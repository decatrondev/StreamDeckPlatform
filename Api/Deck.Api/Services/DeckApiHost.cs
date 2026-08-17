using Deck.Api.Auth;
using Deck.Core.Credentials;
using Deck.Core.Data;
using Deck.Core.Execution;
using Deck.Core.Model;
using Deck.Core.Plugins;
using Deck.Core.SystemActions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Deck.Api.Services;

// Punto único de arranque del Core para la API — mismo rol que
// Deck.UI.Avalonia/Services/DeckAppService, pero pensado para un proceso que
// atiende requests concurrentes: en vez de un único DbContext de larga vida
// (seguro en el hilo único de la UI, no acá) usa un IDbContextFactory, así
// cada request/mensaje de hub abre su propio contexto corto sobre el mismo
// archivo SQLite.
//
// Simplificación consciente de esta fase: la API corre su propia base
// ("Flowdeck-Api/flowdeck.db"), separada de la que usa el Virtual Deck de
// escritorio ("Flowdeck/flowdeck.db"). Unificar ambos procesos en un único
// Core compartido (para que el escritorio y la Web Deck reflejen literalmente
// el mismo estado en vivo) queda para una fase posterior si hace falta —
// EF Core no está pensado para dos procesos escribiendo el mismo SQLite a la
// vez sin coordinación extra.
public sealed class DeckApiHost : IAsyncDisposable
{
    public IDbContextFactory<DeckDbContext> DbFactory { get; }
    public PluginManager Plugins { get; }
    public ActionExecutor Executor { get; }
    public string PairingKey { get; }

    private DeckApiHost(IDbContextFactory<DeckDbContext> dbFactory, PluginManager plugins, ActionExecutor executor, string pairingKey)
    {
        DbFactory = dbFactory;
        Plugins = plugins;
        Executor = executor;
        PairingKey = pairingKey;
    }

    public static async Task<DeckApiHost> StartAsync(string sqliteFilePath, ILoggerFactory loggerFactory)
    {
        var options = DeckDb.CreateOptions(sqliteFilePath);
        var dataDirectory = Path.GetDirectoryName(sqliteFilePath)!;

        using (var migrationDb = new DeckDbContext(options))
        {
            DeckDb.EnsureMigrated(migrationDb);
            await SeedIfEmptyAsync(migrationDb);
        }

        var dbFactory = new PooledDbContextFactory<DeckDbContext>(options);

        var credentialsDb = new DeckDbContext(options);
        var credentials = new SqliteCredentialManager(
            credentialsDb,
            CredentialEncryptionKey.LoadOrCreate(Path.Combine(dataDirectory, "credentials.key")));

        var plugins = new PluginManager(credentials, loggerFactory);
        var systemPlugin = plugins.LoadInstance(new SystemActionsPlugin());
        await plugins.InitializeAsync(systemPlugin.Metadata.Id);
        await plugins.ConnectAsync(systemPlugin.Metadata.Id);

        var pairingKey = Auth.PairingKey.LoadOrCreate(Path.Combine(dataDirectory, "pairing.key"));

        return new DeckApiHost(dbFactory, plugins, new ActionExecutor(plugins), pairingKey);
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

    public async ValueTask DisposeAsync()
    {
        foreach (var plugin in Plugins.Plugins)
        {
            try { await Plugins.DisconnectAsync(plugin.Metadata.Id); } catch { /* al cerrar, no bloquea el shutdown */ }
        }
    }
}
