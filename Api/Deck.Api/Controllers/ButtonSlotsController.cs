using Deck.Api.Dtos;
using Deck.Api.Services;
using Deck.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Deck.Api.Controllers;

[ApiController]
[Route("api/pages/{pageId:guid}/buttons")]
public class ButtonSlotsController : ControllerBase
{
    private readonly DeckApiHost _host;

    public ButtonSlotsController(DeckApiHost host) => _host = host;

    // Upsert por (pageId, row, column): así el cliente arma la grilla de una
    // vez sin tener que crear cada slot vacío por adelantado.
    [HttpPut("{row:int}/{column:int}")]
    public async Task<ActionResult<ButtonSlotDto>> Upsert(Guid pageId, int row, int column, UpsertButtonSlotRequest request)
    {
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(validationError);

        await using var db = await _host.DbFactory.CreateDbContextAsync();

        if (!await db.Pages.AnyAsync(p => p.Id == pageId)) return NotFound($"No existe ninguna Page con id '{pageId}'.");

        if (request.Type == ButtonSlotType.Folder && request.TargetPageId is { } targetId &&
            !await db.Pages.AnyAsync(p => p.Id == targetId))
        {
            return BadRequest($"No existe ninguna Page con id '{targetId}' para usar como destino de la carpeta.");
        }

        var slot = await db.ButtonSlots.FirstOrDefaultAsync(b => b.PageId == pageId && b.Row == row && b.Column == column);
        if (slot is null)
        {
            slot = new ButtonSlot { Id = Guid.NewGuid(), PageId = pageId, Row = row, Column = column };
            db.ButtonSlots.Add(slot);
        }
        else
        {
            db.ActionSteps.RemoveRange(db.ActionSteps.Where(s => s.ButtonSlotId == slot.Id));
        }

        slot.Type = request.Type;
        slot.TargetPageId = request.Type == ButtonSlotType.Folder ? request.TargetPageId : null;
        slot.Label = request.Label;
        slot.IconRef = request.IconRef;

        var stepDtos = request.Steps ?? [];
        var steps = stepDtos
            .Select(s => new ActionStep
            {
                Id = Guid.NewGuid(),
                ButtonSlotId = slot.Id,
                Order = s.Order,
                PluginId = s.PluginId,
                ActionId = s.ActionId,
                ParametersJson = s.ParametersJson
            })
            .ToList();

        if (request.Type == ButtonSlotType.Action) db.ActionSteps.AddRange(steps);

        await db.SaveChangesAsync();

        return ButtonSlotDto.From(slot, request.Type == ButtonSlotType.Action ? steps.Select(ActionStepDto.From).ToList() : []);
    }

    [HttpDelete("{row:int}/{column:int}")]
    public async Task<IActionResult> Delete(Guid pageId, int row, int column)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();
        var slot = await db.ButtonSlots.FirstOrDefaultAsync(b => b.PageId == pageId && b.Row == row && b.Column == column);
        if (slot is null) return NotFound();

        db.ActionSteps.RemoveRange(db.ActionSteps.Where(s => s.ButtonSlotId == slot.Id));
        db.ButtonSlots.Remove(slot);
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static string? Validate(UpsertButtonSlotRequest request)
    {
        if (request.Type == ButtonSlotType.Folder && request.TargetPageId is null)
        {
            return "Un botón de tipo Folder necesita TargetPageId.";
        }

        if (request.Type == ButtonSlotType.Action && request.TargetPageId is not null)
        {
            return "Un botón de tipo Action no puede tener TargetPageId.";
        }

        if (request.Type == ButtonSlotType.Folder && request.Steps is { Count: > 0 })
        {
            return "Un botón de tipo Folder no puede tener ActionStep — navega, no ejecuta.";
        }

        return null;
    }
}
