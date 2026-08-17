namespace Deck.SDK.Plugins;

// Resultado de ejecutar una acción — nunca una excepción cruzando hacia el
// Core. Un fallo de plugin se reporta acá, no revienta el proceso principal.
public sealed record PluginActionResult(bool Success, string? Message = null)
{
    public static PluginActionResult Ok(string? message = null) => new(true, message);
    public static PluginActionResult Fail(string message) => new(false, message);
}
