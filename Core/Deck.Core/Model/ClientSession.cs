namespace Deck.Core.Model;

public enum ClientType
{
    VirtualDeck,
    WebDeck,
    MobileDeck,
    Hardware
}

// Estado de navegación de un cliente conectado. Profile/Page/ButtonSlot/etc.
// son la definición compartida (persistida); ClientSession es lo único que es
// por cliente — así el deck físico y el celular pueden estar parados en
// pantallas distintas al mismo tiempo sin pisarse.
public class ClientSession
{
    public Guid Id { get; set; }
    public ClientType ClientType { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public Guid ActiveProfileId { get; set; }
    public Guid ActivePageId { get; set; }
    public DateTime ConnectedAt { get; set; }
}
