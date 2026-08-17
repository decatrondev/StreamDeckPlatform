namespace MobileDeck.Core;

public sealed class DeckApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public bool IsAuthError => StatusCode == 401;
}
