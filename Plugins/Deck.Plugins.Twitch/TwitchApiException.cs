namespace Deck.Plugins.Twitch;

public class TwitchApiException : Exception
{
    public int StatusCode { get; }

    public TwitchApiException(int statusCode, string body)
        : base($"Twitch devolvió {statusCode}: {body}")
    {
        StatusCode = statusCode;
    }
}

// 401 puntual — token vencido a mitad de sesión. El llamador decide si
// refresca y reintenta (igual que en Spotify).
public class TwitchUnauthorizedException : Exception
{
}
