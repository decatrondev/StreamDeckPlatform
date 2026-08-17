namespace Deck.Plugins.Twitch;

public sealed record TwitchTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt - TimeSpan.FromSeconds(30);
}
