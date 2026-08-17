using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Deck.Api.Tests;

// La API lee Deck:DatabasePath de IConfiguration ANTES de que
// WebApplicationBuilder.Build() se llame (arranca el Core ahí mismo, ver
// Program.cs) — para cuando WithWebHostBuilder podría inyectar overrides ya
// es tarde. La única forma confiable de aislar cada corrida de test es una
// variable de entorno, leída por CreateBuilder(args) desde el arranque mismo.
//
// Por eso todos los tests de este proyecto viven en una sola clase con un solo
// IClassFixture: si dos factories corrieran en paralelo, se pisarían la
// variable de entorno entre sí.
public sealed class DeckApiFactory : WebApplicationFactory<Program>
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"deck-api-tests-{Guid.NewGuid():N}");

    public string DbPath { get; }

    public DeckApiFactory()
    {
        DbPath = Path.Combine(_tempDir, "flowdeck.db");
        Environment.SetEnvironmentVariable("Deck__DatabasePath", DbPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("Deck__DatabasePath", null);

        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }
}
