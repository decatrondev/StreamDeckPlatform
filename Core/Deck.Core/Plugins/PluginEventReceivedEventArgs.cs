using Deck.SDK.Plugins;

namespace Deck.Core.Plugins;

public sealed class PluginEventReceivedEventArgs : EventArgs
{
    public required string PluginId { get; init; }
    public required PluginEvent Event { get; init; }
}
