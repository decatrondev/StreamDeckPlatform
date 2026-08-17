using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deck.Core.Model;

namespace Deck.UI.Avalonia.ViewModels;

// Envuelve un ButtonSlot para una posición (Row, Column) de la grilla. Slot es
// null cuando la posición todavía no tiene nada asignado — recién se persiste
// al asignarle una acción o carpeta (ver MainViewModel.OnDialogClosedAsync).
public partial class ButtonSlotViewModel : ViewModelBase
{
    public ButtonSlot? Slot { get; private set; }
    public int Row { get; }
    public int Column { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    public partial string? Label { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    public partial bool IsFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    public partial bool IsRunning { get; set; }

    public bool IsAssigned => Slot is not null;

    public string DisplayLabel => Label is { Length: > 0 } label ? label : "+";

    public string StatusGlyph => IsRunning ? "…" : IsFolder ? "▸" : "";

    public IAsyncRelayCommand ActivateCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand ClearCommand { get; }

    public event Func<ButtonSlotViewModel, Task>? Activated;
    public event Func<ButtonSlotViewModel, Task>? EditRequested;
    public event Func<ButtonSlotViewModel, Task>? ClearRequested;

    public ButtonSlotViewModel(int row, int column, ButtonSlot? slot)
    {
        Row = row;
        Column = column;
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
        OnPropertyChanged(nameof(IsAssigned));
        EditCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }
}
