namespace Deck.Plugins.Obs;

public class ObsRequestException : Exception
{
    public int StatusCode { get; }

    public ObsRequestException(int statusCode, string comment)
        : base($"OBS request falló (código {statusCode}): {comment}")
    {
        StatusCode = statusCode;
    }
}
