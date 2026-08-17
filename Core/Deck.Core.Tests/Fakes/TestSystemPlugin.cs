using System.Diagnostics;
using System.Text.Json;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Core.Tests.Fakes;

// Plugin falso usado solo para validar el pipeline completo del Core (carga,
// ciclo de vida, ejecución, aislamiento de errores) sin depender de un plugin
// real — el entregable de Fase 1 pide probar con acciones mock: "abrir una
// app, ejecutar un comando del sistema".
public class TestSystemPlugin : IPlugin
{
    private IPluginContext? _context;

    public PluginMetadata Metadata { get; } = new(
        Id: "test-system",
        Name: "Test System Plugin",
        Version: "0.0.0",
        Author: "tests");

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("open-app", "Abrir aplicación"),
        new("run-command", "Ejecutar comando del sistema"),
        new("fail", "Falla siempre (para probar aislamiento de errores)")
    ];

    public event EventHandler<PluginEvent>? EventRaised;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        EventRaised?.Invoke(this, new PluginEvent("connected", "{}", DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        return actionId switch
        {
            "open-app" => await OpenAppAsync(parametersJson),
            "run-command" => await RunCommandAsync(parametersJson),
            "fail" => PluginActionResult.Fail("Falla intencional de prueba."),
            _ => PluginActionResult.Fail($"Acción desconocida: '{actionId}'.")
        };
    }

    private async Task<PluginActionResult> OpenAppAsync(string parametersJson)
    {
        var (path, args) = ParseCommand(parametersJson);

        using var process = Process.Start(new ProcessStartInfo(path, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null) return PluginActionResult.Fail($"No se pudo iniciar '{path}'.");

        await process.WaitForExitAsync();
        return process.ExitCode == 0
            ? PluginActionResult.Ok($"'{path}' abierto (PID {process.Id}).")
            : PluginActionResult.Fail($"'{path}' terminó con código {process.ExitCode}.");
    }

    private async Task<PluginActionResult> RunCommandAsync(string parametersJson)
    {
        var (path, args) = ParseCommand(parametersJson);

        using var process = Process.Start(new ProcessStartInfo(path, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null) return PluginActionResult.Fail($"No se pudo ejecutar '{path}'.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? PluginActionResult.Ok(stdout.Trim())
            : PluginActionResult.Fail($"Comando terminó con código {process.ExitCode}.");
    }

    private static (string path, string args) ParseCommand(string parametersJson)
    {
        var doc = JsonDocument.Parse(parametersJson);
        var path = doc.RootElement.GetProperty("path").GetString()!;
        var args = doc.RootElement.TryGetProperty("args", out var argsEl) ? argsEl.GetString() ?? "" : "";
        return (path, args);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
