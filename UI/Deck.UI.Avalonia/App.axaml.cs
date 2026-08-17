using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
