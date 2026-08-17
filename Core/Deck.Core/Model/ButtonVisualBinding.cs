namespace Deck.Core.Model;

// Un ButtonSlot puede suscribirse a un evento de plugin solo para actualizar su
// apariencia (ícono/color), independiente de si ese mismo evento también
// dispara un Trigger. Ejecutar y pintar son responsabilidades separadas.
public class ButtonVisualBinding
{
    public Guid Id { get; set; }
    public Guid ButtonSlotId { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;

    // Cómo se traduce el payload del evento a apariencia (ej. mapeo de estado a
    // ícono/color) — se define recién cuando el motor de eventos se implemente.
    public string MappingJson { get; set; } = "{}";
}
