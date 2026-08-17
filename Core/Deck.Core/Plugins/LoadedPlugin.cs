using System.Runtime.Loader;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Core.Plugins;

public class LoadedPlugin
{
    public required IPlugin Instance { get; init; }
    public required PluginMetadata Metadata { get; init; }
    public PluginState State { get; set; } = PluginState.Loaded;
    public string? LastError { get; set; }

    // Null para plugins registrados in-process (ej. en tests) — no se pueden
    // descargar en caliente, solo los cargados dinámicamente desde un .dll.
    internal AssemblyLoadContext? LoadContext { get; init; }
}
