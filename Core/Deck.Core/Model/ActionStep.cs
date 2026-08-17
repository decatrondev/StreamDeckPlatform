namespace Deck.Core.Model;

// Un paso dentro de la lista ordenada de acciones de un ButtonSlot o un Trigger.
// ParametersJson queda tipado recién del lado del plugin (Deck.SDK define el
// contrato en Fase 1) — acá solo se persiste como texto.
public class ActionStep
{
    public Guid Id { get; set; }

    // Dueño del paso: viene de un ButtonSlot o de un Trigger, nunca de ambos.
    public Guid? ButtonSlotId { get; set; }
    public Guid? TriggerId { get; set; }

    public int Order { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
}
