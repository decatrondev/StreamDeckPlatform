namespace Deck.Plugins.Discord;

public class DiscordWebhookException : Exception
{
    public int StatusCode { get; }

    public DiscordWebhookException(int statusCode, string body)
        : base($"El webhook de Discord devolvió {statusCode}: {body}")
    {
        StatusCode = statusCode;
    }
}
