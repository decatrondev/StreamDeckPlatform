using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Core.Tests.FakePlugin;

// Compilado a un .dll aparte a propósito: es lo que usa PluginManagerTests
// para probar la carga dinámica real (AssemblyLoadContext), no solo el
// registro in-process de TestSystemPlugin.
public class DynamicFakePlugin : IPlugin
{
    public PluginMetadata Metadata { get; } = new(
        Id: "dynamic-fake",
        Name: "Dynamic Fake Plugin",
        Version: "0.0.0",
        Author: "tests");

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
        [new("ping", "Devuelve pong")];

#pragma warning disable CS0067 // no lo dispara nadie: este plugin no emite eventos, solo prueba la carga dinámica
    public event EventHandler<PluginEvent>? EventRaised;
#pragma warning restore CS0067

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default) => Task.CompletedTask;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default) =>
        Task.FromResult(actionId == "ping" ? PluginActionResult.Ok("pong") : PluginActionResult.Fail("unknown action"));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
