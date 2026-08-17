using Deck.Core.Model;
using Deck.Core.Plugins;
using Deck.SDK.Plugins;

namespace Deck.Core.Execution;

// Motor de ejecución: corre la lista ordenada de ActionStep de un botón o
// trigger, un paso a la vez, a través del PluginManager (que ya aísla
// excepciones de plugin). Decisión: se corta en el primer paso que falla — una
// cadena tipo "mutear mic" + "cambiar escena" no debe seguir a la escena si
// mutear falló, el orden de los pasos es intencional.
public class ActionExecutor
{
    private readonly PluginManager _plugins;

    public ActionExecutor(PluginManager plugins)
    {
        _plugins = plugins;
    }

    public async Task<ActionExecutionResult> RunAsync(IEnumerable<ActionStep> steps, CancellationToken ct = default)
    {
        var ordered = steps.OrderBy(s => s.Order).ToList();
        var results = new List<PluginActionResult>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var step = ordered[i];
            var result = await _plugins.ExecuteActionAsync(step.PluginId, step.ActionId, step.ParametersJson, ct);
            results.Add(result);

            if (!result.Success)
            {
                return new ActionExecutionResult(Success: false, results, FailedAtStep: i);
            }
        }

        return new ActionExecutionResult(Success: true, results, FailedAtStep: null);
    }
}
