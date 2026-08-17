using System.Collections.Concurrent;
using Deck.Core.Model;

namespace Deck.Api.Services;

// Estado de navegación en memoria por conexión SignalR — un celular y una
// pestaña de Web Deck pueden estar parados en páginas distintas del mismo
// perfil sin pisarse (ver Deck.Core/Model/ClientSession.cs). No se persiste:
// si el proceso reinicia, cada cliente vuelve a mandar su página activa al
// reconectar.
public sealed class ClientSessionRegistry
{
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();

    public ClientSession Connect(string connectionId, ClientType clientType)
    {
        var session = new ClientSession
        {
            Id = Guid.NewGuid(),
            ClientType = clientType,
            ConnectionId = connectionId,
            ConnectedAt = DateTime.UtcNow
        };

        _sessions[connectionId] = session;
        return session;
    }

    public void Disconnect(string connectionId) => _sessions.TryRemove(connectionId, out _);

    public ClientSession? Get(string connectionId) => _sessions.GetValueOrDefault(connectionId);

    public void SetActivePage(string connectionId, Guid profileId, Guid pageId)
    {
        if (!_sessions.TryGetValue(connectionId, out var session)) return;
        session.ActiveProfileId = profileId;
        session.ActivePageId = pageId;
    }

    public IReadOnlyCollection<ClientSession> All => (IReadOnlyCollection<ClientSession>)_sessions.Values;
}
