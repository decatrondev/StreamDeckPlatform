using Deck.Api.Dtos;
using Deck.Api.Services;
using Deck.Core.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Deck.Api.Hubs;

// Canal en vivo entre la Web/Mobile Deck y el Core: apretar un botón tiene que
// sentirse instantáneo, así que la ejecución real (no solo el CRUD de
// edición) va por acá y no por REST. Los eventos de plugin (ver Program.cs,
// donde se conecta PluginManager.PluginEventReceived) se retransmiten a todos
// los clientes conectados por el mismo canal.
public class DeckHub : Hub
{
    private readonly DeckApiHost _host;
    private readonly ClientSessionRegistry _sessions;

    public DeckHub(DeckApiHost host, ClientSessionRegistry sessions)
    {
        _host = host;
        _sessions = sessions;
    }

    public override Task OnConnectedAsync()
    {
        var clientTypeHeader = Context.GetHttpContext()?.Request.Query["clientType"].ToString();
        var clientType = Enum.TryParse<ClientType>(clientTypeHeader, ignoreCase: true, out var parsed)
            ? parsed
            : ClientType.WebDeck;

        _sessions.Connect(Context.ConnectionId, clientType);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _sessions.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public void SetActivePage(Guid profileId, Guid pageId) =>
        _sessions.SetActivePage(Context.ConnectionId, profileId, pageId);

    public async Task<ExecuteButtonResult> ExecuteButton(Guid pageId, int row, int column)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();

        var slot = await db.ButtonSlots.FirstOrDefaultAsync(b => b.PageId == pageId && b.Row == row && b.Column == column);
        if (slot is null) return new ExecuteButtonResult(false, null, null, "No hay ningún botón asignado en esa posición.");

        if (slot.Type == ButtonSlotType.Folder)
        {
            return new ExecuteButtonResult(true, slot.TargetPageId, null, null);
        }

        var steps = await db.ActionSteps
            .Where(s => s.ButtonSlotId == slot.Id)
            .OrderBy(s => s.Order)
            .ToListAsync();

        var result = await _host.Executor.RunAsync(steps);

        return new ExecuteButtonResult(
            result.Success,
            null,
            result.StepResults.Select(r => new ActionStepResultDto(r.Success, r.Message)).ToList(),
            result.Success ? null : result.StepResults.ElementAtOrDefault(result.FailedAtStep ?? -1)?.Message);
    }
}
