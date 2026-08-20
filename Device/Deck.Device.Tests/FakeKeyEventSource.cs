namespace Deck.Device.Tests;

// Dispara líneas a mano, sin puerto serial real — el mismo rol que cumplen
// los Fake*Server de los plugins (OBS, Spotify, Discord, Twitch).
public sealed class FakeKeyEventSource : IKeyEventSource
{
    public event Action<string>? LineReceived;
    public bool Opened { get; private set; }

    public void Open() => Opened = true;

    public void Emit(string line) => LineReceived?.Invoke(line);

    public void Dispose() { }
}
