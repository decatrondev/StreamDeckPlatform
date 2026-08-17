namespace Deck.Plugins.Spotify;

public class SpotifyApiException : Exception
{
    public int StatusCode { get; }

    public SpotifyApiException(int statusCode, string body)
        : base($"Spotify devolvió {statusCode}: {body}")
    {
        StatusCode = statusCode;
    }
}
