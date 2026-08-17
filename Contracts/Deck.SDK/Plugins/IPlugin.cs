namespace Deck.SDK.Plugins;

// El contrato mínimo que todo plugin implementa (Deck.SDK, definido en Fase 1,
// validado con el primer plugin real en Fase 3 — OBS).
//
// Reglas del contrato:
//  - Un fallo de conexión o excepción interna se reporta al Core (vía
//    PluginActionResult / eventos de error), nunca revienta el proceso.
//  - Reconexión automática: el plugin debe poder perder conexión y reintentar
//    sin intervención manual — el Core no reintenta por él.
//  - Sin credenciales propias: todo lo que necesite auth pasa por
//    IPluginContext.Credentials.
public interface IPlugin : IAsyncDisposable
{
    PluginMetadata Metadata { get; }

    IReadOnlyList<PluginActionDescriptor> Actions { get; }

    // Se emite hacia el Core — estado de conexión, eventos del servicio externo, etc.
    event EventHandler<PluginEvent>? EventRaised;

    Task InitializeAsync(IPluginContext context, CancellationToken ct = default);

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default);
}
