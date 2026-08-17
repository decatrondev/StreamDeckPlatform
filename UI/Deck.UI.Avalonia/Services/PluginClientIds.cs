namespace Deck.UI.Avalonia.Services;

// Client IDs reales de las apps ya registradas del bot de Decatron — no son
// secretos (Twitch/Spotify usan OAuth PKCE, Discord usa RPC local), así que
// son seguros de embeber en este repo público. Los ClientSecret/BotToken de
// esas mismas apps NUNCA deben tocar este proyecto.
//
// Twitch y Spotify igual no van a poder completar el login hasta que se
// registre un redirect URI local (ej. http://127.0.0.1:<puerto>/callback)
// en el dashboard de cada plataforma — hoy solo tienen registrado el de
// twitch.decatron.net. Discord no tiene este problema (RPC local, sin
// redirect URI).
internal static class PluginClientIds
{
    public const string Discord = "1166234674419474433";
    public const string Twitch = "84pudubhtwz6ax651d5wlm7nzf326v";
    public const string Spotify = "c8c0045024564d63a17468f78892aa6c";

    // App "Flowdeck" registrada en https://decatron.net/developer/apps/new
    // (redirect URI http://127.0.0.1:51823/callback/, scope read:profile).
    // El client_secret que dio esa misma pantalla NO va acá ni en ningún
    // lado del repo — el login usa PKCE puro.
    public const string Decatron = "deca_PwtaVcrycItIevaY3Qob";
}
