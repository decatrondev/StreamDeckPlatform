namespace Deck.Plugins.Spotify;

public class SpotifyAuthException : Exception
{
    public SpotifyAuthException(string message) : base(message)
    {
    }
}
