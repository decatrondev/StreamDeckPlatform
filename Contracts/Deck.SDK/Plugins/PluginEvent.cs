namespace Deck.SDK.Plugins;

// Lo que un plugin emite hacia el Core (ej. "stream en vivo", "canción cambió")
// para que un ButtonVisualBinding o un Trigger reaccionen.
public sealed record PluginEvent(string EventId, string PayloadJson, DateTimeOffset OccurredAt);
