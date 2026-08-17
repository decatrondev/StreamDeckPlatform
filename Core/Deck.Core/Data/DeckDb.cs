using Microsoft.EntityFrameworkCore;

namespace Deck.Core.Data;

// Punto único para armar las DbContextOptions reales (Deck.UI.Avalonia y
// Deck.Api lo van a usar cuando se conecten a Deck.Core — todavía no, eso es
// trabajo de integración de Fase 2 / Fase 7).
public static class DeckDb
{
    public static DbContextOptions<DeckDbContext> CreateOptions(string sqliteFilePath)
    {
        var directory = Path.GetDirectoryName(sqliteFilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        return new DbContextOptionsBuilder<DeckDbContext>()
            .UseSqlite($"Data Source={sqliteFilePath}")
            .Options;
    }

    public static void EnsureMigrated(DeckDbContext db) => db.Database.Migrate();
}
