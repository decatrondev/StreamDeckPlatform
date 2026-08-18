using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deck.Core.Icons;
using Deck.SDK.Plugins;

namespace Deck.UI.Avalonia.ViewModels;

public enum AssignMode
{
    Action,
    Folder
}

public sealed record AssignActionResult(
    AssignMode Mode, string? PluginId, string? ActionId, string Label,
    string? PathOrUrl, string? Args, string? RawParametersJson, string? IconRef);

// Lo que ya tenía asignado la tecla, para precargar el diálogo al editar en
// vez de abrirlo vacío (ver MainViewModel.LoadExistingAssignmentAsync).
public sealed record ExistingAssignment(
    AssignMode Mode, string? PluginId, string? ActionId, string Label,
    string? PathOrUrl, string? Args, string? RawParametersJson, string? IconRef);

public partial class AssignActionDialogViewModel : ViewModelBase
{
    private const string SystemPluginId = "system";

    private readonly IconStore? _icons;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial string? IconRef { get; set; }

    [ObservableProperty]
    public partial Bitmap? IconPreviewBitmap { get; set; }

    [ObservableProperty]
    public partial string? IconPreviewEmoji { get; set; }

    public bool HasIcon => IconPreviewBitmap is not null || IconPreviewEmoji is not null;

    public bool IsSystemAction => SelectedAction.PluginId == SystemPluginId;

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand<string> SelectBuiltInIconCommand { get; }
    public IRelayCommand ClearIconCommand { get; }

    public event Action<AssignActionResult?>? Closed;

    // El diseñador de Avalonia instancia esto sin argumentos.
    public AssignActionDialogViewModel() : this([]) { }

    public AssignActionDialogViewModel(
        IReadOnlyList<(string PluginId, string PluginName, PluginActionDescriptor Action)> pluginActions,
        IconStore? icons = null,
        ExistingAssignment? existing = null)
    {
        _icons = icons;

        AvailableActions.Add(new(SystemPluginId, "open-app", "Abrir aplicación", "Ruta del ejecutable"));
        AvailableActions.Add(new(SystemPluginId, "run-command", "Ejecutar comando", "Comando"));
        AvailableActions.Add(new(SystemPluginId, "open-url", "Abrir URL", "URL"));

        foreach (var (pluginId, pluginName, action) in pluginActions)
        {
            AvailableActions.Add(new(pluginId, action.Id, $"{pluginName}: {action.Name}", action.Description ?? action.Name));
        }

        SelectedAction = existing is { Mode: AssignMode.Action, PluginId: not null }
            ? AvailableActions.FirstOrDefault(a => a.PluginId == existing.PluginId && a.ActionId == existing.ActionId)
              ?? AvailableActions[0]
            : AvailableActions[0];

        if (existing is not null)
        {
            Mode = existing.Mode;
            Label = existing.Label;
            PathOrUrl = existing.PathOrUrl ?? "";
            Args = existing.Args ?? "";
            RawParametersJson = existing.RawParametersJson ?? "{}";
            IconRef = existing.IconRef;
        }

        RefreshIconPreview();

        SaveCommand = new RelayCommand(Save, CanSave);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(null));
        SelectBuiltInIconCommand = new RelayCommand<string>(emoji => IconRef = $"emoji:{emoji}");
        ClearIconCommand = new RelayCommand(() => IconRef = null);

        PropertyChanged += (_, args) =>
        {
            SaveCommand.NotifyCanExecuteChanged();
            if (args.PropertyName == nameof(SelectedAction)) OnPropertyChanged(nameof(IsSystemAction));
            if (args.PropertyName == nameof(IconRef)) RefreshIconPreview();
        };
    }

    // Elegir un archivo es una operación de plataforma (StorageProvider) que
    // vive en el code-behind de la ventana — acá solo se guarda el resultado.
    public async Task SetCustomIconAsync(Stream fileStream, string extension, CancellationToken ct = default)
    {
        if (_icons is null) return;
        IconRef = await _icons.SaveCustomIconAsync(fileStream, extension, ct);
    }

    private void RefreshIconPreview()
    {
        IconPreviewEmoji = IconStore.ResolveEmoji(IconRef);

        var filePath = _icons?.ResolveFilePath(IconRef);
        IconPreviewBitmap = null;
        if (filePath is not null && File.Exists(filePath))
        {
            try { IconPreviewBitmap = new Bitmap(filePath); }
            catch { /* archivo corrupto o formato no soportado */ }
        }
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
            IsSystemAction ? null : RawParametersJson.Trim(),
            IconRef));
    }

    public sealed record ActionOption(string PluginId, string ActionId, string Name, string ParameterLabel)
    {
        public override string ToString() => Name;
    }
}
