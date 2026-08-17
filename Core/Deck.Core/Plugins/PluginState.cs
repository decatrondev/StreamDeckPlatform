namespace Deck.Core.Plugins;

public enum PluginState
{
    Loaded,
    Initializing,
    Ready,
    Connecting,
    Connected,
    Disconnected,
    Faulted
}
