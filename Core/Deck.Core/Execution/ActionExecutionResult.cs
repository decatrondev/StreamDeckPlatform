using Deck.SDK.Plugins;

namespace Deck.Core.Execution;

// Resultado de correr la lista ordenada de ActionStep de un botón o un trigger.
public sealed record ActionExecutionResult(
    bool Success,
    IReadOnlyList<PluginActionResult> StepResults,
    int? FailedAtStep
);
