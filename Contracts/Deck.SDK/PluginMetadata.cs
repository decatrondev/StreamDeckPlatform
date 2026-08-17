namespace Deck.SDK;

// Lo que un plugin declara de sí mismo — el Core lo usa para listarlo en la UI
// y para decidir qué permisos conceder, sin tener que instanciarlo primero.
public record PluginMetadata(
    string Id,
    string Name,
    string Version,
    string Author,
    string? IconRef = null,
    IReadOnlyList<string>? Permissions = null
);
