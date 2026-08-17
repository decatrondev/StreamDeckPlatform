namespace Deck.Core.Credentials;

// Credential Manager central del Core (principio 3 / sección 5 del contrato de
// plugin): todo plugin que necesita auth pasa por acá, ninguno guarda secretos
// por su cuenta. Cada plugin solo ve su propio namespace (PluginId + Key) —
// se lo expone ScopedCredentialStore, nunca esta interfaz completa.
public interface ICredentialManager
{
    Task<string?> GetAsync(string pluginId, string key, CancellationToken ct = default);
    Task SetAsync(string pluginId, string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string pluginId, string key, CancellationToken ct = default);
}
