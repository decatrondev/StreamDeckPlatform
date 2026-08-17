using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deck.UI.Avalonia.ViewModels;

public enum AssignMode
{
    Action,
    Folder
}

public sealed record AssignActionResult(AssignMode Mode, string? ActionId, string Label, string? PathOrUrl, string? Args);

public partial class AssignActionDialogViewModel : ViewModelBase
{
    public ObservableCollection<ActionOption> AvailableActions { get; } =
    [
        new("open-app", "Abrir aplicación", "Ruta del ejecutable"),
        new("run-command", "Ejecutar comando", "Comando"),
        new("open-url", "Abrir URL", "URL")
    ];

    [ObservableProperty]
    public partial AssignMode Mode { get; set; } = AssignMode.Action;

    [ObservableProperty]
    public partial ActionOption SelectedAction { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PathOrUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Args { get; set; } = string.Empty;

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public event Action<AssignActionResult?>? Closed;

    public AssignActionDialogViewModel()
    {
        SelectedAction = AvailableActions[0];
        SaveCommand = new RelayCommand(Save, CanSave);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(null));

        PropertyChanged += (_, _) => SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(Label) &&
        (Mode == AssignMode.Folder || !string.IsNullOrWhiteSpace(PathOrUrl));

    private void Save()
    {
        Closed?.Invoke(new AssignActionResult(
            Mode,
            Mode == AssignMode.Action ? SelectedAction.ActionId : null,
            Label.Trim(),
            string.IsNullOrWhiteSpace(PathOrUrl) ? null : PathOrUrl.Trim(),
            string.IsNullOrWhiteSpace(Args) ? null : Args.Trim()));
    }

    public sealed record ActionOption(string ActionId, string Name, string ParameterLabel)
    {
        public override string ToString() => Name;
    }
}
