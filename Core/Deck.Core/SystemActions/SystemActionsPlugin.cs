using System.Diagnostics;
using System.Text.Json;
using Deck.SDK;
using Deck.SDK.Plugins;

namespace Deck.Core.SystemActions;

// No es un plugin de terceros ni de integración externa — son las acciones
// nativas del sistema operativo (abrir app, ejecutar comando, abrir URL) que
// la Fase 2 necesita para que los botones hagan algo real sin depender de
// ningún plugin de Fase 3+. Se registra con el mismo PluginManager que
// cualquier otro plugin (mismo pipeline, mismo aislamiento de errores) — la
// única diferencia es que lo instancia el propio Core, no se carga desde un
// .dll externo.
public class SystemActionsPlugin : IPlugin
{
    public const string PluginId = "system";

    public PluginMetadata Metadata { get; } = new(
        Id: PluginId,
        Name: "Sistema",
        Version: "1.0.0",
        Author: "Flowdeck");

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("open-app", "Abrir aplicación", "Lanza un ejecutable local."),
        new("run-command", "Ejecutar comando", "Corre un comando de sistema y espera a que termine."),
        new("open-url", "Abrir URL", "Abre una URL con la app default del sistema.")
    ];

    public event EventHandler<PluginEvent>? EventRaised
    {
        add { }
        remove { }
    }

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default) => Task.CompletedTask;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, string parametersJson, CancellationToken ct = default)
    {
        try
        {
            return actionId switch
            {
                "open-app" => await OpenAppAsync(parametersJson),
                "run-command" => await RunCommandAsync(parametersJson),
                "open-url" => OpenUrl(parametersJson),
                _ => PluginActionResult.Fail($"Acción desconocida: '{actionId}'.")
            };
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail(ex.Message);
        }
    }

    private static async Task<PluginActionResult> OpenAppAsync(string parametersJson)
    {
        var path = GetRequiredString(parametersJson, "path");
        var args = GetOptionalString(parametersJson, "args");

        using var process = Process.Start(new ProcessStartInfo(path, args ?? "") { UseShellExecute = true });
        if (process is null) return PluginActionResult.Fail($"No se pudo abrir '{path}'.");

        await Task.CompletedTask; // se lanza y no se espera: "abrir", no "correr y esperar"
        return PluginActionResult.Ok($"'{path}' abierto.");
    }

    private static async Task<PluginActionResult> RunCommandAsync(string parametersJson)
    {
        var path = GetRequiredString(parametersJson, "path");
        var args = GetOptionalString(parametersJson, "args") ?? "";

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
            ? PluginActionResult.Ok(string.IsNullOrWhiteSpace(stdout) ? "Comando ejecutado." : stdout.Trim())
            : PluginActionResult.Fail($"Comando terminó con código {process.ExitCode}.");
    }

    private static PluginActionResult OpenUrl(string parametersJson)
    {
        var url = GetRequiredString(parametersJson, "url");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return PluginActionResult.Ok($"'{url}' abierta.");
    }

    private static string GetRequiredString(string json, string property)
    {
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(property).GetString()
            ?? throw new InvalidOperationException($"El parámetro '{property}' no puede ser nulo.");
    }

    private static string? GetOptionalString(string json, string property)
    {
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
