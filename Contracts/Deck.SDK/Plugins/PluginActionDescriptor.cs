namespace Deck.SDK.Plugins;

// Metadata de una acción que el plugin expone — para listarla en la UI antes
// de ejecutarla. ParametersSchemaJson es JSON Schema, opcional: si el plugin no
// lo da, la UI trata los parámetros como JSON libre.
public sealed record PluginActionDescriptor(
    string Id,
    string Name,
    string? Description = null,
    string? ParametersSchemaJson = null
);
