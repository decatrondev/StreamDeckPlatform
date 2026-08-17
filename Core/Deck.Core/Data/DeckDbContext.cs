using Deck.Core.Credentials;
using Deck.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace Deck.Core.Data;

// Persistencia local (SQLite) de la definición compartida: perfiles, páginas,
// botones, acciones y triggers. ClientSession NO vive acá — es estado de
// navegación en memoria, por cliente conectado, no se persiste (ver
// Model/ClientSession.cs).
public class DeckDbContext : DbContext
{
    public DeckDbContext(DbContextOptions<DeckDbContext> options) : base(options)
    {
    }

    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<ButtonSlot> ButtonSlots => Set<ButtonSlot>();
    public DbSet<ActionStep> ActionSteps => Set<ActionStep>();
    public DbSet<Trigger> Triggers => Set<Trigger>();
    public DbSet<ButtonVisualBinding> ButtonVisualBindings => Set<ButtonVisualBinding>();
    public DbSet<StoredCredential> Credentials => Set<StoredCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>()
            .HasIndex(p => p.Name);

        modelBuilder.Entity<ButtonSlot>()
            .HasIndex(b => new { b.PageId, b.Row, b.Column })
            .IsUnique();

        modelBuilder.Entity<ActionStep>()
            .HasIndex(a => new { a.ButtonSlotId, a.Order });

        modelBuilder.Entity<ActionStep>()
            .HasIndex(a => new { a.TriggerId, a.Order });

        modelBuilder.Entity<StoredCredential>()
            .HasIndex(c => new { c.PluginId, c.Key })
            .IsUnique();
    }
}
