using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deck.SDK.Plugins;

namespace Deck.UI.Avalonia.ViewModels;

public enum AssignMode
{
    Action,
    Folder
}

public sealed record AssignActionResult(
    AssignMode Mode, string? PluginId, string? ActionId, string Label,
    string? PathOrUrl, string? Args, string? RawParametersJson);

public partial class AssignActionDialogViewModel : ViewModelBase
{
    private const string SystemPluginId = "system";

    public ObservableCollection<ActionOption> AvailableActions { get; } = [];

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

    // Solo se usa para acciones de plugins que no son las 3 nativas del
    // sistema — esos plugins no publican todavía un ParametersSchemaJson (ver
    // Deck.SDK.Plugins.PluginActionDescriptor) que permita armar un formulario
    // dedicado por acción, así que el parámetro se escribe a mano como JSON
    // crudo. Simplificación consciente: hasta que haya un schema real, es
    // esto o no poder usar esas acciones desde la UI en absoluto.
    [ObservableProperty]
    public partial string RawParametersJson { get; set; } = "{}";

    public bool IsSystemAction => SelectedAction.PluginId == SystemPluginId;

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public event Action<AssignActionResult?>? Closed;

    // El diseñador de Avalonia instancia esto sin argumentos.
    public AssignActionDialogViewModel() : this([]) { }

    public AssignActionDialogViewModel(IReadOnlyList<(string PluginId, string PluginName, PluginActionDescriptor Action)> pluginActions)
    {
        AvailableActions.Add(new(SystemPluginId, "open-app", "Abrir aplicación", "Ruta del ejecutable"));
        AvailableActions.Add(new(SystemPluginId, "run-command", "Ejecutar comando", "Comando"));
        AvailableActions.Add(new(SystemPluginId, "open-url", "Abrir URL", "URL"));

        foreach (var (pluginId, pluginName, action) in pluginActions)
        {
            AvailableActions.Add(new(pluginId, action.Id, $"{pluginName}: {action.Name}", action.Description ?? action.Name));
        }

        SelectedAction = AvailableActions[0];
        SaveCommand = new RelayCommand(Save, CanSave);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(null));

        PropertyChanged += (_, args) =>
        {
            SaveCommand.NotifyCanExecuteChanged();
            if (args.PropertyName == nameof(SelectedAction)) OnPropertyChanged(nameof(IsSystemAction));
        };
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(Label) &&
        (Mode == AssignMode.Folder || !IsSystemAction || !string.IsNullOrWhiteSpace(PathOrUrl));

    private void Save()
    {
        Closed?.Invoke(new AssignActionResult(
            Mode,
            Mode == AssignMode.Action ? SelectedAction.PluginId : null,
            Mode == AssignMode.Action ? SelectedAction.ActionId : null,
            Label.Trim(),
            string.IsNullOrWhiteSpace(PathOrUrl) ? null : PathOrUrl.Trim(),
            string.IsNullOrWhiteSpace(Args) ? null : Args.Trim(),
            IsSystemAction ? null : RawParametersJson.Trim()));
    }

    public sealed record ActionOption(string PluginId, string ActionId, string Name, string ParameterLabel)
    {
        public override string ToString() => Name;
    }
}
