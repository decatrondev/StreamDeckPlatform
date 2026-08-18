using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Deck.UI.Avalonia.Services;
using Deck.UI.Avalonia.ViewModels;
using Deck.UI.Avalonia.Views;

namespace Deck.UI.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Primero se muestra el splash y recién después arranca todo lo
            // demás (chequeo de actualizaciones, datos, ventana principal) —
            // así el usuario ve feedback inmediato en vez de una demora en
            // blanco. Si ApplyUpdatesAndRestart llega a dispararse dentro de
            // RunStartupAsync, el proceso termina ahí mismo y esta ventana
            // nunca llega a cerrarse — no hace falta contemplarlo acá.
            var splashViewModel = new SplashViewModel();
            var splash = new SplashWindow { DataContext = splashViewModel };
            desktop.MainWindow = splash;
            splash.Show();

            _ = RunStartupAsync(desktop, splash, splashViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunStartupAsync(
        IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash, SplashViewModel splashViewModel)
    {
        var restartArgs = desktop.Args ?? [];
        await new UpdateService().CheckAndApplyBeforeLaunchAsync(
            restartArgs, status => splashViewModel.StatusText = status);

        // Se resuelve antes de mostrar la ventana principal: sin datos
        // todavía no hay nada que mostrar, y es rápido (SQLite local).
        var app = await DeckAppService.StartAsync();
        var mainViewModel = new MainViewModel(app);
        await mainViewModel.InitializeAsync();

        var mainWindow = new MainWindow { DataContext = mainViewModel };
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();

        // Si el usuario intenta abrir Flowdeck de nuevo mientras ya está
        // abierto, SingleInstanceGuard cierra el proceso nuevo y nos avisa
        // acá — la pedimos maximizada porque es la forma más obvia de que
        // quede claro que "ya está abierta, acá está".
        SingleInstanceGuard.ListenForActivationRequests(() =>
            Dispatcher.UIThread.Post(() =>
            {
                mainWindow.WindowState = WindowState.Maximized;
                mainWindow.Activate();
            }));
    }
}
