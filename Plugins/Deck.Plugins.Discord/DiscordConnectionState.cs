namespace Deck.Plugins.Discord;

public enum DiscordConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    AuthenticationFailed
}
