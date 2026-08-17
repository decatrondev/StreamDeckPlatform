namespace Deck.SDK.Credentials;

// Vista del Credential Manager central que el Core le pasa a cada plugin, ya
// aislada por plugin — el plugin no ve ni puede tocar credenciales de otro.
// Ningún plugin persiste secretos por su cuenta (principio del contrato, Fase 1).
public interface ICredentialStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
