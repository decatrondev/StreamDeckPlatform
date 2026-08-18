namespace Deck.SDK.Plugins;

// Una opción concreta para un campo "select" dinámico de una acción — ej.
// una escena real de OBS. Value es lo que se persiste en ParametersJson,
// Label es lo que ve el usuario (acá suelen ser el mismo string, pero se
// separan por si algún plugin necesita un id distinto del nombre visible).
public sealed record ParameterOption(string Value, string Label)
{
    // Sin esto, el ComboBox de la UI (que no le pone un ItemTemplate propio)
    // muestra el ToString() autogenerado del record — "ParameterOption {
    // Value = ..., Label = ... }" — en vez del nombre limpio.
    public override string ToString() => Label;
}
