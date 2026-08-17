using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Deck.Core.Model;
using Deck.UI.Avalonia.ViewModels;

namespace Deck.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Sin barra de título nativa: esto es lo que hace que la ventana se pueda
    // arrastrar tomándola desde nuestra propia franja superior.
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is Profile profile)
        {
            await vm.SelectProfileAsync(profile);
        }
    }
}
