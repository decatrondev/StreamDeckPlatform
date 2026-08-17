using System.Security.Claims;
using System.Text.Encodings.Web;
using Deck.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Deck.Api.Auth;

// Valida el secreto compartido contra dos lugares posibles: el header
// "Authorization: Bearer <key>" (REST, y cualquier cliente HTTP normal) o el
// query string "access_token" (SignalR: el navegador no puede setear headers
// custom en el handshake de WebSocket, así que el cliente JS de SignalR manda
// el token ahí — ver accessTokenFactory del lado de Web/Mobile Deck).
public sealed class PairingKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PairingKey";

    private readonly DeckApiHost _host;

    public PairingKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, DeckApiHost host)
        : base(options, logger, encoder)
    {
        _host = host;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractToken(Request);

        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Falta la pairing key."));
        }

        if (!CryptographicOperations.FixedTimeEquals(token, _host.PairingKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Pairing key inválida."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "paired-client")], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static string? ExtractToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        return request.Query.TryGetValue("access_token", out var fromQuery) ? fromQuery.ToString() : null;
    }
}

file static class CryptographicOperations
{
    // Comparación en tiempo constante — evitar que un atacante deduzca la key
    // byte a byte midiendo cuánto tarda cada intento fallido.
    public static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
