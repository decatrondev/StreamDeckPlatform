using Avalonia;
using System;
using Velopack;

namespace Deck.UI.Avalonia;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Tiene que ser la primera línea de todas, antes de tocar cualquier
        // API de Avalonia — Velopack necesita interceptar argumentos propios
        // que el instalador/actualizador le pasa al proceso en ciertos
        // momentos del ciclo de vida (sobre todo en Windows). Si algo más
        // corre antes, esos hooks nunca se disparan.
        VelopackApp.Build().Run();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
