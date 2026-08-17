using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Deck.Core.Data;

// Solo para `dotnet ef migrations` — en runtime real el DbContextOptions se
// arma con la ruta de datos real de la app (ver Deck.UI.Avalonia / Deck.Api),
// no acá.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DeckDbContext>
{
    public DeckDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DeckDbContext>()
            .UseSqlite("Data Source=deck.design.db")
            .Options;

        return new DeckDbContext(options);
    }
}
