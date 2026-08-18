using CommunityToolkit.Mvvm.ComponentModel;

namespace Deck.UI.Avalonia.ViewModels;

public partial class SplashViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Verificando actualizaciones…";
}
