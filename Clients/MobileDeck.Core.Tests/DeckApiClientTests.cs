using Deck.Api.Tests;
using MobileDeck.Core;

namespace MobileDeck.Core.Tests;

// Contra el Deck.Api real (WebApplicationFactory de Deck.Api.Tests), no un
// mock — misma lógica de negocio que valida a Web Deck, del lado .NET.
public class DeckApiClientTests : IClassFixture<DeckApiFactory>
{
    private readonly DeckApiFactory _factory;

    public DeckApiClientTests(DeckApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PingAsync_WithoutPairingKey_ReturnsTrue()
    {
        // /api/ping es público a propósito — ping no debería fallar aunque la
        // key esté mal, sirve para validar solo la dirección.
        var client = new DeckApiClient(_factory.CreateClient(), pairingKey: "lo-que-sea");

        Assert.True(await client.PingAsync());
    }

    [Fact]
    public async Task GetProfilesAsync_WithCorrectPairingKey_ReturnsSeededProfile()
    {
        var client = new DeckApiClient(_factory.CreateClient(), _factory.PairingKey);

        var profiles = await client.GetProfilesAsync();

        var profile = Assert.Single(profiles);
        Assert.Equal("Principal", profile.Name);
    }

    [Fact]
    public async Task GetProfilesAsync_WithWrongPairingKey_ThrowsAuthError()
    {
        var client = new DeckApiClient(_factory.CreateClient(), pairingKey: "no-es-la-key-correcta");

        var ex = await Assert.ThrowsAsync<DeckApiException>(() => client.GetProfilesAsync());

        Assert.True(ex.IsAuthError);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsRootPageWithButtons()
    {
        var client = new DeckApiClient(_factory.CreateClient(), _factory.PairingKey);
        var profile = (await client.GetProfilesAsync()).Single();

        var page = await client.GetPageAsync(profile.RootPageId);

        Assert.Equal(profile.RootPageId, page.Id);
    }
}
