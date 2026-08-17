namespace Deck.Plugins.Obs;

// Opcodes del protocolo obs-websocket v5 (https://github.com/obsproject/obs-websocket).
public static class ObsOpCode
{
    public const int Hello = 0;
    public const int Identify = 1;
    public const int Identified = 2;
    public const int Reidentify = 3;
    public const int Event = 5;
    public const int Request = 6;
    public const int RequestResponse = 7;
    public const int RequestBatch = 8;
    public const int RequestBatchResponse = 9;
}
