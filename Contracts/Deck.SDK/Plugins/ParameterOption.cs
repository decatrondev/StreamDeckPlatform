namespace Deck.SDK.Plugins;

// Una opción concreta para un campo "select" dinámico de una acción — ej.
// una escena real de OBS. Value es lo que se persiste en ParametersJson,
// Label es lo que ve el usuario (acá suelen ser el mismo string, pero se
// separan por si algún plugin necesita un id distinto del nombre visible).
public sealed record ParameterOption(string Value, string Label);
