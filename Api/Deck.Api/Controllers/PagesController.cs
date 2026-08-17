using Deck.Api.Dtos;
using Deck.Api.Services;
using Deck.Core.Data;
using Deck.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Deck.Api.Controllers;

[ApiController]
[Route("api/pages")]
public class PagesController : ControllerBase
{
    private readonly DeckApiHost _host;

    public PagesController(DeckApiHost host) => _host = host;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PageDto>> GetById(Guid id)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();

        var page = await db.Pages.FindAsync(id);
        if (page is null) return NotFound();

        var buttons = await LoadButtonsAsync(db, id);
        return PageDto.From(page, buttons);
    }

    [HttpPost]
    public async Task<ActionResult<PageDto>> Create(CreatePageRequest request)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();

        var page = new Page { Id = Guid.NewGuid(), Name = request.Name, Rows = request.Rows, Columns = request.Columns };
        db.Pages.Add(page);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = page.Id }, PageDto.From(page, []));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CreatePageRequest request)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();
        var page = await db.Pages.FindAsync(id);
        if (page is null) return NotFound();

        page.Name = request.Name;
        page.Rows = request.Rows;
        page.Columns = request.Columns;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();
        var page = await db.Pages.FindAsync(id);
        if (page is null) return NotFound();

        var slotIds = await db.ButtonSlots.Where(b => b.PageId == id).Select(b => b.Id).ToListAsync();
        db.ActionSteps.RemoveRange(db.ActionSteps.Where(s => s.ButtonSlotId != null && slotIds.Contains(s.ButtonSlotId.Value)));
        db.ButtonSlots.RemoveRange(db.ButtonSlots.Where(b => b.PageId == id));
        db.Pages.Remove(page);
        await db.SaveChangesAsync();

        return NoContent();
    }

    internal static async Task<List<ButtonSlotDto>> LoadButtonsAsync(DeckDbContext db, Guid pageId)
    {
        var slots = await db.ButtonSlots.Where(b => b.PageId == pageId).ToListAsync();
        var slotIds = slots.Select(s => s.Id).ToList();

        var steps = await db.ActionSteps
            .Where(s => s.ButtonSlotId != null && slotIds.Contains(s.ButtonSlotId.Value))
            .OrderBy(s => s.Order)
            .ToListAsync();

        return slots
            .Select(slot => ButtonSlotDto.From(
                slot,
                steps.Where(s => s.ButtonSlotId == slot.Id).Select(ActionStepDto.From).ToList()))
            .ToList();
    }
}
