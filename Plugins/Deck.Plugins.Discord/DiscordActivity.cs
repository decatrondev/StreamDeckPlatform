namespace Deck.Plugins.Discord;

// Lo que se manda con SET_ACTIVITY (Rich Presence) — a diferencia de
// mute/deafen/voz, esto no pasa por AUTHORIZE/AUTHENTICATE ni por los
// scopes de voz restringidos de Discord, funciona apenas termina el
// handshake inicial.
public sealed record DiscordActivity(
    string? Details,
    string? State,
    string? LargeImageKey = null,
    string? LargeImageText = null,
    string? SmallImageKey = null,
    string? SmallImageText = null,
    IReadOnlyList<DiscordActivityButton>? Buttons = null);

// Discord solo acepta hasta 2 botones por activity.
public sealed record DiscordActivityButton(string Label, string Url);
