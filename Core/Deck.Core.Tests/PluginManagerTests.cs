using Deck.Core.Credentials;
using Deck.Core.Data;
using Deck.Core.Plugins;
using Deck.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Deck.Core.Tests;

public class PluginManagerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"deck-test-{Guid.NewGuid()}.db");
    private readonly string _keyPath;
    private readonly PluginManager _manager;

    public PluginManagerTests()
    {
        _keyPath = Path.Combine(Path.GetTempPath(), $"deck-test-key-{Guid.NewGuid()}.txt");
        var db = new DeckDbContext(DeckDb.CreateOptions(_dbPath));
        DeckDb.EnsureMigrated(db);

        var credentials = new SqliteCredentialManager(db, CredentialEncryptionKey.LoadOrCreate(_keyPath));
        _manager = new PluginManager(credentials, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Lifecycle_InitializeConnect_MarksPluginReady()
    {
        var loaded = _manager.LoadInstance(new TestSystemPlugin());

        await _manager.InitializeAsync(loaded.Metadata.Id);
        Assert.Equal(PluginState.Ready, loaded.State);

        await _manager.ConnectAsync(loaded.Metadata.Id);
        Assert.Equal(PluginState.Connected, loaded.State);
    }

    [Fact]
    public async Task ConnectAsync_RaisesPluginEvent()
    {
        var loaded = _manager.LoadInstance(new TestSystemPlugin());
        await _manager.InitializeAsync(loaded.Metadata.Id);

        (string PluginId, string EventId)? received = null;
        _manager.PluginEventReceived += (_, e) => received = (e.PluginId, e.Event.EventId);

        await _manager.ConnectAsync(loaded.Metadata.Id);

        Assert.NotNull(received);
        Assert.Equal("test-system", received!.Value.PluginId);
        Assert.Equal("connected", received.Value.EventId);
    }

    [Fact]
    public async Task ExecuteActionAsync_RunCommand_ReturnsProcessOutput()
    {
        var loaded = _manager.LoadInstance(new TestSystemPlugin());
        await _manager.InitializeAsync(loaded.Metadata.Id);

        var (path, args) = EchoCommand("hola flowdeck");
        var result = await _manager.ExecuteActionAsync(
            loaded.Metadata.Id, "run-command", JsonSerializer.Serialize(new { path, args }));

        Assert.True(result.Success);
        Assert.Contains("hola flowdeck", result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_OpenApp_LaunchesProcessSuccessfully()
    {
        var loaded = _manager.LoadInstance(new TestSystemPlugin());
        await _manager.InitializeAsync(loaded.Metadata.Id);

        // "Abrir una app" del entregable de Fase 1 — acá una CLI garantizada en
        // el PATH de cualquier runner (Windows/Linux/macOS) en vez de una app
        // gráfica, para que el test sea determinístico en CI.
        var path = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var args = OperatingSystem.IsWindows() ? "/c exit 0" : "-c \"exit 0\"";

        var result = await _manager.ExecuteActionAsync(
            loaded.Metadata.Id, "open-app", JsonSerializer.Serialize(new { path, args }));

        Assert.True(result.Success);
        Assert.Contains(path, result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_FailingAction_DoesNotThrow_ReturnsFailResult()
    {
        // Un plugin que falla no debe tumbar el proceso — se reporta como
        // resultado, nunca como excepción no capturada (principio de
        // aislamiento de errores).
        var loaded = _manager.LoadInstance(new TestSystemPlugin());
        await _manager.InitializeAsync(loaded.Metadata.Id);

        var result = await _manager.ExecuteActionAsync(loaded.Metadata.Id, "fail", "{}");

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void LoadFromAssemblyPath_LoadsPluginDynamically()
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "Deck.Core.Tests.FakePlugin.dll");
        Assert.True(File.Exists(dllPath), $"No se encontró el .dll compilado en '{dllPath}'.");

        var loaded = _manager.LoadFromAssemblyPath(dllPath);

        Assert.Equal("dynamic-fake", loaded.Metadata.Id);
        Assert.NotNull(_manager.Get("dynamic-fake"));
    }

    private static (string path, string args) EchoCommand(string message) =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c echo {message}")
            : ("/bin/sh", $"-c \"echo {message}\"");

    public void Dispose()
    {
        File.Delete(_dbPath);
        File.Delete(_keyPath);
    }
}
