using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deck.Core.Icons;
using Deck.Core.Model;

namespace Deck.UI.Avalonia.ViewModels;

// Envuelve un ButtonSlot para una posición (Row, Column) de la grilla. Slot es
// null cuando la posición todavía no tiene nada asignado — recién se persiste
// al asignarle una acción o carpeta (ver MainViewModel.OnDialogClosedAsync).
public partial class ButtonSlotViewModel : ViewModelBase
{
    private readonly IconStore? _icons;

    public ButtonSlot? Slot { get; private set; }
    public int Row { get; }
    public int Column { get; }

    // Fila 0 / columna 0 de cualquier página que no sea la raíz — reservada
    // para volver a la carpeta de afuera, no se puede editar ni asignar nada
    // ahí (ver MainViewModel.LoadCurrentPageAsync). Si esa posición ya tenía
    // algo asignado de antes, esa asignación deja de usarse.
    public bool IsBackButton { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    public partial string? Label { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    public partial bool IsFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial Bitmap? IconBitmap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial string? IconEmoji { get; set; }

    public bool IsAssigned => Slot is not null;

    // Con ícono, la tecla lo muestra a pantalla completa y el label de texto
    // no se pinta — ver el template de la grilla en MainWindow.axaml.
    public bool HasIcon => IconBitmap is not null || IconEmoji is not null;

    public string DisplayLabel => IsBackButton ? "Volver" : Label is { Length: > 0 } label ? label : "+";

    public string StatusGlyph => IsRunning ? "…" : IsFolder ? "▸" : "";

    public IAsyncRelayCommand ActivateCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand ClearCommand { get; }

    public event Func<ButtonSlotViewModel, Task>? Activated;
    public event Func<ButtonSlotViewModel, Task>? EditRequested;
    public event Func<ButtonSlotViewModel, Task>? ClearRequested;

    public ButtonSlotViewModel(int row, int column, ButtonSlot? slot, IconStore? icons = null, bool isBackButton = false)
    {
        Row = row;
        Column = column;
        IsBackButton = isBackButton;
        _icons = icons;
        ActivateCommand = new AsyncRelayCommand(() => Activated?.Invoke(this) ?? Task.CompletedTask);
        EditCommand = new AsyncRelayCommand(() => EditRequested?.Invoke(this) ?? Task.CompletedTask, () => IsAssigned);
        ClearCommand = new AsyncRelayCommand(() => ClearRequested?.Invoke(this) ?? Task.CompletedTask, () => IsAssigned);
        Apply(slot);
    }

    public void Apply(ButtonSlot? slot)
    {
        Slot = slot;
        Label = slot?.Label;
        IsFolder = slot?.Type == ButtonSlotType.Folder;

        IconEmoji = IconStore.ResolveEmoji(slot?.IconRef);
        var filePath = _icons?.ResolveFilePath(slot?.IconRef);
        IconBitmap = null;
        if (filePath is not null && File.Exists(filePath))
        {
            try { IconBitmap = new Bitmap(filePath); }
            catch { /* archivo corrupto o formato no soportado — se ve sin ícono, no rompe la grilla */ }
        }

        OnPropertyChanged(nameof(IsAssigned));
        EditCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }
}
