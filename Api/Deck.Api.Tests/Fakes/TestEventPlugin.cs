using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Api.Tests.Fakes;

// Plugin mínimo solo para probar el relay Core -> SignalR -> todos los
// clientes conectados: SystemActionsPlugin (el único registrado por defecto
// en Deck.Api) implementa EventRaised vacío a propósito, así que no sirve
// para este caso.
public sealed class TestEventPlugin : IPlugin
{
    public PluginMetadata Metadata { get; } = new(
        Id: "test-event-plugin", Name: "Test Event Plugin", Version: "0.0.0", Author: "tests");

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } = [];

    public event EventHandler<PluginEvent>? EventRaised;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default) => Task.CompletedTask;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        EventRaised?.Invoke(this, new PluginEvent("test-event", """{"ok":true}""", DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default) =>
        Task.FromResult(PluginActionResult.Fail("Este plugin no tiene acciones."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
