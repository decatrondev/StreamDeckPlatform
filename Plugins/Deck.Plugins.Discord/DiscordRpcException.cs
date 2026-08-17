namespace Deck.Plugins.Discord;

public class DiscordRpcException : Exception
{
    public int Code { get; }

    public DiscordRpcException(int code, string message) : base(message)
    {
        Code = code;
    }
}
