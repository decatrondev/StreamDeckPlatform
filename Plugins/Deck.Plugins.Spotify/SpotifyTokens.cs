namespace Deck.Plugins.Spotify;

public sealed record SpotifyTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt - TimeSpan.FromSeconds(30);
}
