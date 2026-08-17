namespace Deck.Core.Model;

// Escucha un evento de un plugin (ej. Twitch: nuevo follow) y dispara su propia
// lista ordenada de ActionStep, sin depender de que nadie toque un botón. Es la
// pieza de automatización real, separada de la navegación por botones.
public class Trigger
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PluginId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string? FilterJson { get; set; }
    public bool Enabled { get; set; } = true;
}
