using Deck.Api.Dtos;
using Deck.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Deck.Api.Controllers;

[ApiController]
[Route("api/plugins")]
public class PluginsController : ControllerBase
{
    private readonly DeckApiHost _host;

    public PluginsController(DeckApiHost host) => _host = host;

    [HttpGet]
    public ActionResult<IReadOnlyList<PluginDto>> GetAll() =>
        _host.Plugins.Plugins.Select(PluginDto.From).ToList();

    [HttpGet("{id}")]
    public ActionResult<PluginDto> GetById(string id)
    {
        var plugin = _host.Plugins.Get(id);
        return plugin is null ? NotFound() : PluginDto.From(plugin);
    }

    [HttpPost("{id}/connect")]
    public async Task<IActionResult> Connect(string id)
    {
        if (_host.Plugins.Get(id) is null) return NotFound();
        await _host.Plugins.ConnectAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/disconnect")]
    public async Task<IActionResult> Disconnect(string id)
    {
        if (_host.Plugins.Get(id) is null) return NotFound();
        await _host.Plugins.DisconnectAsync(id);
        return NoContent();
    }
}
