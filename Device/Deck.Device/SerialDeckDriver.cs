using System.Text.RegularExpressions;
using Deck.Core.Data;
using Deck.Core.Execution;
using Deck.Core.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Deck.Device;

// Traduce eventos del dispositivo físico (KEY:<indice 0-14>:DOWN/UP) a lo
// mismo que hace MainViewModel.OnButtonActivatedAsync en el Virtual Deck:
// carpeta -> navega, acción -> corre sus ActionStep por ActionExecutor. Vive
// aparte de la UI a propósito — el deck físico tiene que poder disparar
// acciones aunque la ventana del Virtual Deck no esté abierta.
public sealed class SerialDeckDriver : IDisposable
{
    // Layout fijo del modelo Pro/Standard: 3 filas x 5 columnas (ver
    // 03-deck-fisico/01-plan.md). El índice que manda el firmware es
    // row-major: idx = fila*5 + columna.
    private const int Columns = 5;

    private static readonly Regex KeyLine = new(@"^KEY:(?<idx>\d+):(?<state>DOWN|UP)$", RegexOptions.Compiled);

    private readonly IKeyEventSource _source;
    private readonly DeckDbContext _db;
    private readonly ActionExecutor _executor;
    private readonly ILogger<SerialDeckDriver> _logger;

    private Guid _currentPageId;

    // Historial de navegación — mismo rol que _breadcrumb en MainViewModel.
    // Vacío = estamos en la raíz.
    private readonly Stack<Guid> _breadcrumb = new();

    // Observable para tests y, a futuro, feedback en la propia UI (ej. "la
    // última tecla física ejecutada falló"). row/column identifican qué
    // tecla corrió.
    public event Action<int, int, ActionExecutionResult>? StepsExecuted;

    public Guid CurrentPageId => _currentPageId;

    public SerialDeckDriver(
        IKeyEventSource source, DeckDbContext db, ActionExecutor executor, Guid rootPageId, ILogger<SerialDeckDriver> logger)
    {
        _source = source;
        _db = db;
        _executor = executor;
        _currentPageId = rootPageId;
        _logger = logger;
        _source.LineReceived += OnLineReceived;
    }

    public void Start() => _source.Open();

    private void OnLineReceived(string line) => _ = ProcessLineAsync(line);

    // Público (no solo por el evento del IKeyEventSource) para que los tests
    // puedan await-earlo directo, sin depender del fire-and-forget de
    // OnLineReceived ni de sleeps para esperar a que termine.
    public Task ProcessLineAsync(string line)
    {
        var match = KeyLine.Match(line);
        if (!match.Success)
        {
            _logger.LogDebug("Línea del dispositivo ignorada (no matchea el protocolo): {Line}", line);
            return Task.CompletedTask;
        }

        // Solo DOWN dispara — igual que un click de mouse, no hace falta
        // reaccionar también al UP (el firmware lo manda para feedback visual
        // propio, no para el protocolo con Flowdeck).
        if (match.Groups["state"].Value != "DOWN") return Task.CompletedTask;

        var idx = int.Parse(match.Groups["idx"].Value);
        var row = idx / Columns;
        var column = idx % Columns;

        return HandleKeyDownAsync(row, column);
    }

    private async Task HandleKeyDownAsync(int row, int column)
    {
        try
        {
            // Misma reserva que MainViewModel: (0,0) es "Volver" en cualquier
            // página que no sea la raíz, pisa lo que hubiera asignado ahí.
            if (row == 0 && column == 0 && _breadcrumb.Count > 0)
            {
                _currentPageId = _breadcrumb.Pop();
                return;
            }

            var slot = await _db.ButtonSlots
                .FirstOrDefaultAsync(s => s.PageId == _currentPageId && s.Row == row && s.Column == column);

            if (slot is null) return; // posición sin asignar en esta Page, no hace nada

            if (slot.Type == ButtonSlotType.Folder)
            {
                _breadcrumb.Push(_currentPageId);
                _currentPageId = slot.TargetPageId!.Value;
                return;
            }

            var steps = await _db.ActionSteps.Where(a => a.ButtonSlotId == slot.Id).ToListAsync();
            var result = await _executor.RunAsync(steps);
            if (!result.Success)
            {
                _logger.LogWarning(
                    "Tecla física ({Row},{Column}) falló en el paso {Step}: {Message}",
                    row, column, result.FailedAtStep, result.StepResults[result.FailedAtStep!.Value].Message);
            }
            StepsExecuted?.Invoke(row, column, result);
        }
        catch (Exception ex)
        {
            // Nunca dejar que una tecla rota tumbe el listener — la próxima
            // tecla tiene que seguir andando.
            _logger.LogError(ex, "Error procesando tecla física ({Row},{Column})", row, column);
        }
    }

    public void Dispose() => _source.Dispose();
}
