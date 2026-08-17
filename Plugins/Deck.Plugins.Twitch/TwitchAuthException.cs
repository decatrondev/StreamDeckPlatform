namespace Deck.Plugins.Twitch;

public class TwitchAuthException : Exception
{
    public TwitchAuthException(string message) : base(message)
    {
    }
}
