using Avalonia;
using System;
using Deck.UI.Avalonia.Services;
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

        // Después de Velopack (esos argumentos internos del updater no
        // cuentan como "el usuario abrió la app dos veces") pero antes de
        // construir cualquier ventana — si ya hay una instancia corriendo,
        // esta ni siquiera debe llegar a inicializar Avalonia.
        if (!SingleInstanceGuard.TryAcquire()) return;

        // El chequeo de actualizaciones corre DESPUÉS de esto, ya dentro del
        // ciclo de vida de Avalonia (ver App.axaml.cs) — necesita el
        // Dispatcher de UI corriendo para poder mostrar la ventana de
        // arranque ("Verificando actualizaciones…") mientras espera a GitHub.
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
