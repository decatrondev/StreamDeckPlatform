using System.Reflection;
using System.Runtime.Loader;

namespace Deck.Core.Plugins;

// Un AssemblyLoadContext coleccionable por plugin — permite descargar el .dll
// de memoria cuando el plugin se deshabilita, sin reiniciar el Core. Deck.SDK
// se resuelve contra la copia ya cargada del Core (no se duplica), el resto de
// las dependencias del plugin se resuelven relativas a su propio .dll.
internal class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath, string pluginId)
        : base(name: $"Plugin:{pluginId}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Deck.SDK debe ser el mismo tipo en ambos lados (Core y plugin) para
        // que las interfaces coincidan — se lo dejamos resolver al contexto por
        // default, que apunta a la copia ya cargada del host.
        if (assemblyName.Name == "Deck.SDK") return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : nint.Zero;
    }
}
