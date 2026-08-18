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
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>>? _liveVariablesProvider;

    // liveVariablesProvider es opcional y agnóstico de qué plugin lo resuelve
    // (Deck.Core no puede depender de un plugin concreto como Decatron) — lo
    // arma DeckAppService, que sí conoce las instancias reales.
    public ActionExecutor(
        PluginManager plugins,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>>? liveVariablesProvider = null)
    {
        _plugins = plugins;
        _liveVariablesProvider = liveVariablesProvider;
    }

    public async Task<ActionExecutionResult> RunAsync(IEnumerable<ActionStep> steps, CancellationToken ct = default)
    {
        var ordered = steps.OrderBy(s => s.Order).ToList();
        var results = new List<PluginActionResult>(ordered.Count);

        // Una sola consulta en vivo por ejecución (no una por paso) — todos
        // los pasos de una misma tecla ven el mismo valor de {categoria}, etc.
        IReadOnlyDictionary<string, string>? liveValues = null;
        if (_liveVariablesProvider is not null && ordered.Any(s => TemplateVariables.ContainsLiveToken(s.ParametersJson)))
        {
            try { liveValues = await _liveVariablesProvider(ct); }
            catch { /* sin red, Decatron caído, etc. — sigue con las variables locales nomás */ }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var step = ordered[i];
            var parametersJson = TemplateVariables.Apply(step.ParametersJson, liveValues);
            var result = await _plugins.ExecuteActionAsync(step.PluginId, step.ActionId, parametersJson, ct);
            results.Add(result);

            if (!result.Success)
            {
                return new ActionExecutionResult(Success: false, results, FailedAtStep: i);
            }
        }

        return new ActionExecutionResult(Success: true, results, FailedAtStep: null);
    }
}
