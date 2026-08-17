using System.Diagnostics;
using System.Reflection;

namespace Deck.UI.Installer;

internal static class Program
{
    private const string ResourceName = "Deck.UI.Installer.SetupInner.exe";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var splash = new SplashForm();

        // El trabajo pesado corre en background — si lo hacemos en el hilo de
        // UI, la ventana se queda congelada sin pintar mientras el instalador
        // real trabaja.
        _ = RunInstallAsync(splash);

        Application.Run(splash);
    }

    private static async Task RunInstallAsync(SplashForm splash)
    {
        try
        {
            var innerExePath = ExtractInnerSetup();
            if (innerExePath is null)
            {
                splash.ShowError("No se encontró el instalador. Descargá el instalador de nuevo desde GitHub.");
                return;
            }

            splash.SetStatus("Instalando Flowdeck…");

            var exitCode = await RunSilentAsync(innerExePath);

            try { File.Delete(innerExePath); } catch { /* archivo temporal, no crítico */ }

            if (exitCode != 0)
            {
                splash.ShowError($"La instalación terminó con un error (código {exitCode}). Revisá que tengas permisos de escritura en tu carpeta de usuario.");
                return;
            }

            splash.SetStatus("Abriendo Flowdeck…");
            LaunchAppIfNotRunning();

            await Task.Delay(600);
            splash.CloseFromBackground();
        }
        catch (Exception ex)
        {
            splash.ShowError($"No se pudo instalar Flowdeck: {ex.Message}");
        }
    }

    private static string? ExtractInnerSetup()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null) return null;

        var tempPath = Path.Combine(Path.GetTempPath(), $"flowdeck-setup-{Guid.NewGuid():N}.exe");
        using (var file = File.Create(tempPath))
        {
            stream.CopyTo(file);
        }

        return tempPath;
    }

    private static async Task<int> RunSilentAsync(string exePath)
    {
        using var process = Process.Start(new ProcessStartInfo(exePath, "--silent")
        {
            UseShellExecute = false,
        });

        if (process is null) return -1;

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    // Squirrel/Velopack instala en %LocalAppData%\Flowdeck\current\ y en
    // teoría el propio Setup.exe --silent ya lanza la app al terminar — esto
    // es una red de seguridad para si esa versión de Velopack no lo hace.
    private static void LaunchAppIfNotRunning()
    {
        if (Process.GetProcessesByName("Deck.UI.Avalonia").Length > 0) return;

        var exePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flowdeck", "current", "Deck.UI.Avalonia.exe");

        if (File.Exists(exePath))
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
    }
}
