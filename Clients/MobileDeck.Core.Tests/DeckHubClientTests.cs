using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deck.Api.Tests;
using MobileDeck.Core;

namespace MobileDeck.Core.Tests;

public class DeckHubClientTests : IClassFixture<DeckApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DeckApiFactory _factory;

    public DeckHubClientTests(DeckApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ConnectAsync_WithCorrectPairingKey_Succeeds()
    {
        await using var hub = BuildHubClient(_factory.PairingKey);

        await hub.ConnectAsync();
    }

    [Fact]
    public async Task ConnectAsync_WithWrongPairingKey_Throws()
    {
        await using var hub = BuildHubClient("no-es-la-key-correcta");

        await Assert.ThrowsAnyAsync<Exception>(() => hub.ConnectAsync());
    }

    [Fact]
    public async Task ExecuteButtonAsync_OnActionSlot_RunsRealActionAndReportsSuccess()
    {
        var page = await CreatePageWithActionButtonAsync();

        await using var hub = BuildHubClient(_factory.PairingKey);
        await hub.ConnectAsync();

        var result = await hub.ExecuteButtonAsync(page.Id, 0, 0);

        Assert.True(result.Success, result.Error);
    }

    private DeckHubClient BuildHubClient(string pairingKey) =>
        new(_factory.Server.BaseAddress.ToString(), pairingKey,
            configureHttpOptions: options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler());

    private async Task<PageDto> CreatePageWithActionButtonAsync()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.PairingKey);

        var pageResponse = await client.PostAsJsonAsync("/api/pages", new { name = "Página de test", rows = 1, columns = 1 });
        pageResponse.EnsureSuccessStatusCode();
        var page = (await pageResponse.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;

        var (path, args) = OperatingSystem.IsWindows() ? ("cmd.exe", "/c echo hola") : ("/bin/sh", "-c \"echo hola\"");
        var upsertResponse = await client.PutAsJsonAsync($"/api/pages/{page.Id}/buttons/0/0", new
        {
            row = 0,
            column = 0,
            type = "Action",
            targetPageId = (Guid?)null,
            label = "Saludo",
            iconRef = (string?)null,
            steps = new[] { new { order = 0, pluginId = "system", actionId = "run-command", parametersJson = System.Text.Json.JsonSerializer.Serialize(new { path, args }) } },
        });
        upsertResponse.EnsureSuccessStatusCode();

        return page;
    }
}
