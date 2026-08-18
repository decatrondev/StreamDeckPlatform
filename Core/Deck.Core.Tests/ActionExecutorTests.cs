using Deck.Core.Credentials;
using Deck.Core.Data;
using Deck.Core.Execution;
using Deck.Core.Model;
using Deck.Core.Plugins;
using Deck.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Deck.Core.Tests;

public class ActionExecutorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"deck-test-{Guid.NewGuid()}.db");
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"deck-test-key-{Guid.NewGuid()}.txt");
    private readonly PluginManager _plugins;
    private readonly ActionExecutor _executor;

    public ActionExecutorTests()
    {
        var db = new DeckDbContext(DeckDb.CreateOptions(_dbPath));
        DeckDb.EnsureMigrated(db);

        var credentials = new SqliteCredentialManager(db, CredentialEncryptionKey.LoadOrCreate(_keyPath));
        _plugins = new PluginManager(credentials, NullLoggerFactory.Instance);
        _executor = new ActionExecutor(_plugins);
    }

    [Fact]
    public async Task RunAsync_StopsAtFirstFailure_DoesNotRunLaterSteps()
    {
        var loaded = _plugins.LoadInstance(new TestSystemPlugin());
        await _plugins.InitializeAsync(loaded.Metadata.Id);

        var buttonId = Guid.NewGuid();
        var steps = new[]
        {
            new ActionStep { Id = Guid.NewGuid(), ButtonSlotId = buttonId, Order = 0, PluginId = "test-system", ActionId = "fail", ParametersJson = "{}" },
            new ActionStep { Id = Guid.NewGuid(), ButtonSlotId = buttonId, Order = 1, PluginId = "test-system", ActionId = "run-command", ParametersJson = """{"path":"does-not-matter"}""" }
        };

        var result = await _executor.RunAsync(steps);

        Assert.False(result.Success);
        Assert.Equal(0, result.FailedAtStep);
        Assert.Single(result.StepResults); // el segundo paso nunca corrió
    }

    [Fact]
    public async Task RunAsync_AllStepsSucceed_ReturnsSuccessInOrder()
    {
        var loaded = _plugins.LoadInstance(new TestSystemPlugin());
        await _plugins.InitializeAsync(loaded.Metadata.Id);

        var buttonId = Guid.NewGuid();
        var (path, args) = OperatingSystem.IsWindows() ? ("cmd.exe", "/c echo uno") : ("/bin/sh", "-c \"echo uno\"");

        var steps = new[]
        {
            new ActionStep { Id = Guid.NewGuid(), ButtonSlotId = buttonId, Order = 1, PluginId = "test-system", ActionId = "run-command", ParametersJson = JsonSerializer.Serialize(new { path, args }) },
        };

        var result = await _executor.RunAsync(steps);

        Assert.True(result.Success);
        Assert.Null(result.FailedAtStep);
        Assert.Contains("uno", result.StepResults[0].Message);
    }

    [Fact]
    public async Task RunAsync_ReplacesTemplateVariables_BeforeExecutingStep()
    {
        var loaded = _plugins.LoadInstance(new TestSystemPlugin());
        await _plugins.InitializeAsync(loaded.Metadata.Id);

        var buttonId = Guid.NewGuid();
        var (path, argsTemplate) = OperatingSystem.IsWindows() ? ("cmd.exe", "/c echo {dia}") : ("/bin/sh", "-c \"echo {dia}\"");

        var steps = new[]
        {
            new ActionStep { Id = Guid.NewGuid(), ButtonSlotId = buttonId, Order = 0, PluginId = "test-system", ActionId = "run-command", ParametersJson = JsonSerializer.Serialize(new { path, args = argsTemplate }) },
        };

        var result = await _executor.RunAsync(steps);

        Assert.True(result.Success);
        Assert.DoesNotContain("{dia}", result.StepResults[0].Message);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_keyPath);
    }
}
