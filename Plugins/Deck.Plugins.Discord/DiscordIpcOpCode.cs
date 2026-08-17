namespace Deck.Plugins.Discord;

// Opcodes del framing binario de Discord IPC (4 bytes opcode + 4 bytes largo,
// little-endian, seguidos del payload JSON).
public static class DiscordIpcOpCode
{
    public const int Handshake = 0;
    public const int Frame = 1;
    public const int Close = 2;
    public const int Ping = 3;
    public const int Pong = 4;
}
