using System.Collections.Concurrent;
using System.Reflection;
using Deck.Core.Credentials;
using Deck.SDK.Plugins;
using Microsoft.Extensions.Logging;

namespace Deck.Core.Plugins;

// Gestor central de plugins: carga (dinámica o in-process), ciclo de vida y
// aislamiento de errores. Ningún fallo de un plugin individual debe tumbar el
// Core — todo lo que cruza hacia acá desde un plugin pasa por try/catch.
public class PluginManager
{
    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new();
    private readonly ICredentialManager _credentialManager;
    private readonly ILoggerFactory _loggerFactory;

    public event EventHandler<PluginEventReceivedEventArgs>? PluginEventReceived;

    public PluginManager(ICredentialManager credentialManager, ILoggerFactory loggerFactory)
    {
        _credentialManager = credentialManager;
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyCollection<LoadedPlugin> Plugins => (IReadOnlyCollection<LoadedPlugin>)_plugins.Values;

    public LoadedPlugin? Get(string pluginId) => _plugins.GetValueOrDefault(pluginId);

    // Carga dinámica desde un .dll compilado contra Deck.SDK — el camino real
    // para plugins de terceros o los propios una vez compilados (Fase 3+).
    public LoadedPlugin LoadFromAssemblyPath(string assemblyPath)
    {
        var pluginId = Path.GetFileNameWithoutExtension(assemblyPath);
        var loadContext = new PluginLoadContext(assemblyPath, pluginId);

        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            ?? throw new InvalidOperationException($"'{assemblyPath}' no tiene ninguna clase que implemente IPlugin.");

        var instance = (IPlugin)(Activator.CreateInstance(pluginType)
            ?? throw new InvalidOperationException($"No se pudo instanciar '{pluginType.FullName}' (¿tiene constructor sin parámetros?)."));

        return Register(instance, loadContext);
    }

    // Registra un plugin ya instanciado, sin AssemblyLoadContext propio — usado
    // en tests para validar el pipeline completo sin compilar un .dll aparte.
    public LoadedPlugin LoadInstance(IPlugin instance) => Register(instance, loadContext: null);

    private LoadedPlugin Register(IPlugin instance, PluginLoadContext? loadContext)
    {
        var loaded = new LoadedPlugin
        {
            Instance = instance,
            Metadata = instance.Metadata,
            LoadContext = loadContext
        };

        instance.EventRaised += (_, evt) => OnPluginEvent(loaded.Metadata.Id, evt);

        if (!_plugins.TryAdd(loaded.Metadata.Id, loaded))
        {
            throw new InvalidOperationException($"Ya hay un plugin cargado con id '{loaded.Metadata.Id}'.");
        }

        return loaded;
    }

    public async Task InitializeAsync(string pluginId, CancellationToken ct = default)
    {
        var plugin = RequirePlugin(pluginId);
        plugin.State = PluginState.Initializing;

        var context = new PluginContext(
            new ScopedCredentialStore(_credentialManager, pluginId),
            _loggerFactory.CreateLogger($"Plugin.{pluginId}"));

        await RunIsolated(plugin, () => plugin.Instance.InitializeAsync(context, ct), PluginState.Ready);
    }

    public Task ConnectAsync(string pluginId, CancellationToken ct = default)
    {
        var plugin = RequirePlugin(pluginId);
        plugin.State = PluginState.Connecting;
        return RunIsolated(plugin, () => plugin.Instance.ConnectAsync(ct), PluginState.Connected);
    }

    public Task DisconnectAsync(string pluginId, CancellationToken ct = default)
    {
        var plugin = RequirePlugin(pluginId);
        return RunIsolated(plugin, () => plugin.Instance.DisconnectAsync(ct), PluginState.Disconnected);
    }

    public async Task<PluginActionResult> ExecuteActionAsync(
        string pluginId, string actionId, string parametersJson, CancellationToken ct = default)
    {
        var plugin = RequirePlugin(pluginId);

        try
        {
            return await plugin.Instance.ExecuteActionAsync(actionId, parametersJson, ct);
        }
        catch (Exception ex)
        {
            plugin.LastError = ex.Message;
            return PluginActionResult.Fail($"Excepción en plugin '{pluginId}': {ex.Message}");
        }
    }

    public async Task UnloadAsync(string pluginId, CancellationToken ct = default)
    {
        if (!_plugins.TryRemove(pluginId, out var plugin)) return;

        try
        {
            await plugin.Instance.DisconnectAsync(ct);
        }
        catch
        {
            // Ya se está desconectando de todas formas — no bloquea la descarga.
        }

        await plugin.Instance.DisposeAsync();

        if (plugin.LoadContext is { } loadContext)
        {
            var weakRef = new WeakReference(loadContext);
            loadContext.Unload();

            // Patrón estándar para AssemblyLoadContext.Unload: puede tardar
            // varios ciclos de GC en soltar todas las referencias.
            for (var i = 0; weakRef.IsAlive && i < 10; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }

    private async Task RunIsolated(LoadedPlugin plugin, Func<Task> action, PluginState successState)
    {
        try
        {
            await action();
            plugin.State = successState;
            plugin.LastError = null;
        }
        catch (Exception ex)
        {
            plugin.State = PluginState.Faulted;
            plugin.LastError = ex.Message;
        }
    }

    private void OnPluginEvent(string pluginId, PluginEvent evt) =>
        PluginEventReceived?.Invoke(this, new PluginEventReceivedEventArgs { PluginId = pluginId, Event = evt });

    private LoadedPlugin RequirePlugin(string pluginId) =>
        _plugins.TryGetValue(pluginId, out var plugin)
            ? plugin
            : throw new KeyNotFoundException($"No hay ningún plugin cargado con id '{pluginId}'.");

    private sealed class PluginContext : IPluginContext
    {
        public PluginContext(Deck.SDK.Credentials.ICredentialStore credentials, ILogger logger)
        {
            Credentials = credentials;
            Logger = logger;
        }

        public Deck.SDK.Credentials.ICredentialStore Credentials { get; }
        public ILogger Logger { get; }
    }
}
