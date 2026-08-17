using Deck.SDK.Credentials;

namespace Deck.Core.Credentials;

// Lo que efectivamente recibe cada plugin en su IPluginContext — el
// ICredentialManager completo queda del lado del Core, el plugin solo ve su
// propio namespace.
public class ScopedCredentialStore : ICredentialStore
{
    private readonly ICredentialManager _manager;
    private readonly string _pluginId;

    public ScopedCredentialStore(ICredentialManager manager, string pluginId)
    {
        _manager = manager;
        _pluginId = pluginId;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        _manager.GetAsync(_pluginId, key, ct);

    public Task SetAsync(string key, string value, CancellationToken ct = default) =>
        _manager.SetAsync(_pluginId, key, value, ct);

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _manager.DeleteAsync(_pluginId, key, ct);
}
