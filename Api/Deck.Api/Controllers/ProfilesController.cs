using Deck.Api.Dtos;
using Deck.Api.Services;
using Deck.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Deck.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private readonly DeckApiHost _host;

    public ProfilesController(DeckApiHost host) => _host = host;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProfileDto>>> GetAll()
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();
        var profiles = await db.Profiles.OrderBy(p => p.Name).ToListAsync();
        return profiles.Select(ProfileDto.From).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProfileDto>> GetById(Guid id)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();
        var profile = await db.Profiles.FindAsync(id);
        return profile is null ? NotFound() : ProfileDto.From(profile);
    }

    [HttpPost]
    public async Task<ActionResult<ProfileDto>> Create(CreateProfileRequest request)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();

        if (!await db.Pages.AnyAsync(p => p.Id == request.RootPageId))
        {
            return BadRequest($"No existe ninguna Page con id '{request.RootPageId}'.");
        }

        var profile = new Profile { Id = Guid.NewGuid(), Name = request.Name, RootPageId = request.RootPageId };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, ProfileDto.From(profile));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CreateProfileRequest request)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();
        var profile = await db.Profiles.FindAsync(id);
        if (profile is null) return NotFound();

        if (!await db.Pages.AnyAsync(p => p.Id == request.RootPageId))
        {
            return BadRequest($"No existe ninguna Page con id '{request.RootPageId}'.");
        }

        profile.Name = request.Name;
        profile.RootPageId = request.RootPageId;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await using var db = await _host.DbFactory.CreateDbContextAsync();
        var profile = await db.Profiles.FindAsync(id);
        if (profile is null) return NotFound();

        db.Profiles.Remove(profile);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
