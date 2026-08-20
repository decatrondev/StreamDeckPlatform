using Deck.Core.Credentials;
using Deck.Core.Data;
using Deck.Core.Execution;
using Deck.Core.Model;
using Deck.Core.Plugins;
using Deck.Core.Tests.FakePlugin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deck.Device.Tests;

public class SerialDeckDriverTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"deck-device-test-{Guid.NewGuid()}.db");
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"deck-device-test-key-{Guid.NewGuid()}.txt");
    private readonly DeckDbContext _db;
    private readonly ActionExecutor _executor;

    public SerialDeckDriverTests()
    {
        _db = new DeckDbContext(DeckDb.CreateOptions(_dbPath));
        DeckDb.EnsureMigrated(_db);

        var credentials = new SqliteCredentialManager(_db, CredentialEncryptionKey.LoadOrCreate(_keyPath));
        var plugins = new PluginManager(credentials, NullLoggerFactory.Instance);
        var loaded = plugins.LoadInstance(new DynamicFakePlugin());
        plugins.InitializeAsync(loaded.Metadata.Id).GetAwaiter().GetResult();
        _executor = new ActionExecutor(plugins);
    }

    private (SerialDeckDriver Driver, Guid PageId) CreateDriverWithPage()
    {
        var page = new Page { Id = Guid.NewGuid(), Name = "Test", Rows = 3, Columns = 5 };
        _db.Pages.Add(page);
        _db.SaveChanges();

        var source = new FakeKeyEventSource();
        var driver = new SerialDeckDriver(source, _db, _executor, page.Id, NullLogger<SerialDeckDriver>.Instance);
        return (driver, page.Id);
    }

    [Fact]
    public async Task KeyDown_OnActionSlot_RunsItsSteps()
    {
        var (driver, pageId) = CreateDriverWithPage();

        // idx 7 -> fila 1, columna 2 (7 = 1*5 + 2)
        var slot = new ButtonSlot { Id = Guid.NewGuid(), PageId = pageId, Row = 1, Column = 2, Type = ButtonSlotType.Action };
        _db.ButtonSlots.Add(slot);
        _db.ActionSteps.Add(new ActionStep
        {
            Id = Guid.NewGuid(), ButtonSlotId = slot.Id, Order = 0, PluginId = "dynamic-fake", ActionId = "ping", ParametersJson = "{}"
        });
        await _db.SaveChangesAsync();

        ActionExecutionResult? observed = null;
        driver.StepsExecuted += (_, _, result) => observed = result;

        await driver.ProcessLineAsync("KEY:7:DOWN");

        Assert.NotNull(observed);
        Assert.True(observed!.Success);
        Assert.Contains("pong", observed.StepResults[0].Message);
    }

    [Fact]
    public async Task KeyDown_OnFolderSlot_NavigatesToTargetPage_AndNextKeyUsesNewPage()
    {
        var (driver, pageId) = CreateDriverWithPage();
        var targetPage = new Page { Id = Guid.NewGuid(), Name = "Sub", Rows = 3, Columns = 5 };
        _db.Pages.Add(targetPage);

        // idx 0 -> fila 0, columna 0
        _db.ButtonSlots.Add(new ButtonSlot
        {
            Id = Guid.NewGuid(), PageId = pageId, Row = 0, Column = 0,
            Type = ButtonSlotType.Folder, TargetPageId = targetPage.Id
        });

        // idx 0 en la página destino, para confirmar que después de navegar
        // el driver mira ButtonSlots de targetPage y no de pageId.
        var innerSlot = new ButtonSlot { Id = Guid.NewGuid(), PageId = targetPage.Id, Row = 0, Column = 0, Type = ButtonSlotType.Action };
        _db.ButtonSlots.Add(innerSlot);
        _db.ActionSteps.Add(new ActionStep
        {
            Id = Guid.NewGuid(), ButtonSlotId = innerSlot.Id, Order = 0, PluginId = "dynamic-fake", ActionId = "ping", ParametersJson = "{}"
        });
        await _db.SaveChangesAsync();

        ActionExecutionResult? observed = null;
        driver.StepsExecuted += (_, _, result) => observed = result;

        await driver.ProcessLineAsync("KEY:0:DOWN"); // navega a targetPage
        Assert.Equal(targetPage.Id, driver.CurrentPageId);
        Assert.Null(observed); // navegar no dispara ActionExecutor

        await driver.ProcessLineAsync("KEY:0:DOWN"); // ahora corre innerSlot, no pageId de nuevo
        Assert.NotNull(observed);
        Assert.True(observed!.Success);
    }

    [Fact]
    public async Task KeyDown_OnEmptySlot_DoesNothing_NoException()
    {
        var (driver, pageId) = CreateDriverWithPage();
        var executed = false;
        driver.StepsExecuted += (_, _, _) => executed = true;

        await driver.ProcessLineAsync("KEY:14:DOWN"); // sin ButtonSlot para esa posición

        Assert.False(executed);
        Assert.Equal(pageId, driver.CurrentPageId); // tampoco navegó a ningún lado
    }

    [Theory]
    [InlineData("KEY:0:UP")]
    [InlineData("no-matchea-nada")]
    [InlineData("")]
    public async Task ProcessLineAsync_IgnoresNonActionableLines(string line)
    {
        var (driver, _) = CreateDriverWithPage();
        var executed = false;
        driver.StepsExecuted += (_, _, _) => executed = true;

        await driver.ProcessLineAsync(line); // no debe tirar

        Assert.False(executed);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_keyPath);
    }
}
