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
            // Se resuelve antes de mostrar la ventana: sin datos todavía no hay
            // nada que mostrar, y es rápido (SQLite local).
            var app = DeckAppService.StartAsync().GetAwaiter().GetResult();
            var mainViewModel = new MainViewModel(app);
            mainViewModel.InitializeAsync().GetAwaiter().GetResult();

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
            desktop.MainWindow = mainWindow;

            // Si el usuario intenta abrir Flowdeck de nuevo mientras ya está
            // abierto, SingleInstanceGuard cierra el proceso nuevo y nos
            // avisa acá — la pedimos maximizada porque es la forma más obvia
            // de que quede claro que "ya está abierta, acá está".
            SingleInstanceGuard.ListenForActivationRequests(() =>
                Dispatcher.UIThread.Post(() =>
                {
                    mainWindow.WindowState = WindowState.Maximized;
                    mainWindow.Activate();
                }));

            // En background: no hay razón para demorar la ventana esperando a
            // GitHub. Si hay una actualización, se descarga sola y se aplica
            // recién cuando el proceso cierre (ver UpdateService) — nunca
            // interrumpe la sesión actual.
            var updateService = new UpdateService();
            updateService.UpdateReady += version =>
                Dispatcher.UIThread.Post(() => mainViewModel.NotifyUpdateReady(version));
            _ = updateService.CheckAndPrepareAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
