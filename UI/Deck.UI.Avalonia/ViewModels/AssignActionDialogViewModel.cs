using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
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

public enum ParameterFieldType
{
    Text,
    Number,
    Boolean,
    Select
}

public sealed record AssignActionResult(
    AssignMode Mode, string? PluginId, string? ActionId, string Label,
    string? PathOrUrl, string? Args, string? ParametersJson, string? IconRef);

// Lo que ya tenía asignado la tecla, para precargar el diálogo al editar en
// vez de abrirlo vacío (ver MainViewModel.LoadExistingAssignmentAsync).
public sealed record ExistingAssignment(
    AssignMode Mode, string? PluginId, string? ActionId, string Label,
    string? PathOrUrl, string? Args, string? ParametersJson, string? IconRef);

// Resuelve las opciones en vivo de un campo "select" dinámico (ej. las
// escenas reales de OBS) — lo implementa MainViewModel, que es quien sabe
// cómo llegar a la instancia real del plugin conectado.
public delegate Task<IReadOnlyList<ParameterOption>> ResolveParameterOptions(
    string pluginId, string actionId, string parameterKey, CancellationToken ct);

// Un campo del formulario dinámico armado a partir del ParametersSchemaJson
// de la acción elegida — reemplaza el textarea de JSON crudo que había antes.
public partial class ParameterFieldViewModel(string key, string label, ParameterFieldType type, bool required)
    : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public ParameterFieldType Type { get; } = type;
    public bool Required { get; } = required;
    public string DisplayLabel { get; } = required ? $"{label} *" : label;

    public ObservableCollection<ParameterOption> Options { get; } = [];

    [ObservableProperty]
    public partial string TextValue { get; set; } = "";

    [ObservableProperty]
    public partial bool BoolValue { get; set; }

    [ObservableProperty]
    public partial ParameterOption? SelectedOption { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingOptions { get; set; }

    public bool HasValue => Type switch
    {
        ParameterFieldType.Boolean => true,
        ParameterFieldType.Select => SelectedOption is not null,
        _ => !string.IsNullOrWhiteSpace(TextValue)
    };
}

public partial class AssignActionDialogViewModel : ViewModelBase
{
    private const string SystemPluginId = "system";
    private static readonly JsonSerializerOptions SchemaJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IconStore? _icons;
    private readonly ResolveParameterOptions? _resolveOptions;
    private readonly List<ActionOption> _allActions = [];

    public ObservableCollection<PluginOption> AvailablePlugins { get; } = [];
    public ObservableCollection<ActionOption> AvailableActions { get; } = [];
    public ObservableCollection<ParameterFieldViewModel> Fields { get; } = [];

    [ObservableProperty]
    public partial AssignMode Mode { get; set; } = AssignMode.Action;

    [ObservableProperty]
    public partial PluginOption SelectedPlugin { get; set; }

    [ObservableProperty]
    public partial ActionOption SelectedAction { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PathOrUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Args { get; set; } = string.Empty;

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
        ResolveParameterOptions? resolveOptions = null,
        ExistingAssignment? existing = null)
    {
        _icons = icons;
        _resolveOptions = resolveOptions;

        _allActions.Add(new(SystemPluginId, "open-app", "Abrir aplicación", "Ruta del ejecutable"));
        _allActions.Add(new(SystemPluginId, "run-command", "Ejecutar comando", "Comando"));
        _allActions.Add(new(SystemPluginId, "open-url", "Abrir URL", "URL"));

        foreach (var (pluginId, _, action) in pluginActions)
        {
            _allActions.Add(new(pluginId, action.Id, action.Name, action.Description ?? action.Name, action.ParametersSchemaJson));
        }

        AvailablePlugins.Add(new(SystemPluginId, "Sistema"));
        foreach (var (pluginId, pluginName) in pluginActions.Select(a => (a.PluginId, a.PluginName)).Distinct())
        {
            AvailablePlugins.Add(new(pluginId, pluginName));
        }

        SaveCommand = new RelayCommand(Save, CanSave);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(null));
        SelectBuiltInIconCommand = new RelayCommand<string>(emoji => IconRef = $"emoji:{emoji}");
        ClearIconCommand = new RelayCommand(() => IconRef = null);

        SelectedPlugin = existing?.PluginId is { } pluginIdToSelect
            ? AvailablePlugins.FirstOrDefault(p => p.Id == pluginIdToSelect) ?? AvailablePlugins[0]
            : AvailablePlugins[0];

        RebuildActionsForPlugin(existing?.ActionId);

        if (existing is not null)
        {
            Mode = existing.Mode;
            Label = existing.Label;
            PathOrUrl = existing.PathOrUrl ?? "";
            Args = existing.Args ?? "";
            IconRef = existing.IconRef;
        }

        RebuildFields(existing?.ParametersJson);
        RefreshIconPreview();

        PropertyChanged += (_, args) =>
        {
            SaveCommand.NotifyCanExecuteChanged();
            switch (args.PropertyName)
            {
                case nameof(SelectedAction):
                    OnPropertyChanged(nameof(IsSystemAction));
                    RebuildFields(null);
                    break;
                case nameof(SelectedPlugin):
                    RebuildActionsForPlugin(null);
                    break;
                case nameof(IconRef):
                    RefreshIconPreview();
                    break;
            }
        };
    }

    private void RebuildActionsForPlugin(string? preferredActionId)
    {
        AvailableActions.Clear();
        foreach (var action in _allActions.Where(a => a.PluginId == SelectedPlugin.Id))
        {
            AvailableActions.Add(action);
        }

        SelectedAction = (preferredActionId is not null
            ? AvailableActions.FirstOrDefault(a => a.ActionId == preferredActionId)
            : null) ?? AvailableActions[0];
    }

    // Arma Fields a partir del ParametersSchemaJson de SelectedAction —
    // reemplaza lo que antes era un textarea de JSON crudo. existingParametersJson
    // solo se usa la primera vez (al precargar una tecla ya asignada); un
    // cambio posterior de acción arranca los campos en blanco.
    private void RebuildFields(string? existingParametersJson)
    {
        Fields.Clear();

        var schemaJson = SelectedAction.ParametersSchemaJson;
        if (string.IsNullOrWhiteSpace(schemaJson)) return;

        SchemaDto? schema;
        try { schema = JsonSerializer.Deserialize<SchemaDto>(schemaJson, SchemaJsonOptions); }
        catch { return; }
        if (schema?.Fields is null) return;

        JsonElement? existingValues = null;
        if (!string.IsNullOrWhiteSpace(existingParametersJson))
        {
            try { existingValues = JsonDocument.Parse(existingParametersJson).RootElement; }
            catch { /* valor guardado corrupto — arranca en blanco */ }
        }

        foreach (var sf in schema.Fields)
        {
            var type = sf.Type switch
            {
                "number" => ParameterFieldType.Number,
                "boolean" => ParameterFieldType.Boolean,
                "select" => ParameterFieldType.Select,
                _ => ParameterFieldType.Text
            };

            var field = new ParameterFieldViewModel(sf.Key, sf.Label, type, sf.Required);

            if (existingValues is { } values && values.TryGetProperty(sf.Key, out var existingValue))
            {
                if (type == ParameterFieldType.Boolean && existingValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    field.BoolValue = existingValue.GetBoolean();
                }
                else if (type != ParameterFieldType.Select)
                {
                    field.TextValue = existingValue.ValueKind == JsonValueKind.String
                        ? existingValue.GetString() ?? ""
                        : existingValue.GetRawText();
                }
            }

            field.PropertyChanged += (_, _) => SaveCommand.NotifyCanExecuteChanged();
            Fields.Add(field);

            if (type == ParameterFieldType.Select && sf.Dynamic)
            {
                var preselectValue = existingValues is { } v && v.TryGetProperty(sf.Key, out var ev) && ev.ValueKind == JsonValueKind.String
                    ? ev.GetString()
                    : null;
                _ = LoadFieldOptionsAsync(field, preselectValue);
            }
        }
    }

    private async Task LoadFieldOptionsAsync(ParameterFieldViewModel field, string? preselectValue)
    {
        if (_resolveOptions is null) return;

        field.IsLoadingOptions = true;
        try
        {
            var options = await _resolveOptions(SelectedAction.PluginId, SelectedAction.ActionId, field.Key, CancellationToken.None);
            foreach (var option in options) field.Options.Add(option);

            if (preselectValue is not null)
            {
                field.SelectedOption = field.Options.FirstOrDefault(o => o.Value == preselectValue);
            }
        }
        finally
        {
            field.IsLoadingOptions = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
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
        (Mode == AssignMode.Folder || !IsSystemAction || !string.IsNullOrWhiteSpace(PathOrUrl)) &&
        (Mode == AssignMode.Folder || IsSystemAction || Fields.Where(f => f.Required).All(f => f.HasValue));

    private void Save()
    {
        Closed?.Invoke(new AssignActionResult(
            Mode,
            Mode == AssignMode.Action ? SelectedAction.PluginId : null,
            Mode == AssignMode.Action ? SelectedAction.ActionId : null,
            Label.Trim(),
            string.IsNullOrWhiteSpace(PathOrUrl) ? null : PathOrUrl.Trim(),
            string.IsNullOrWhiteSpace(Args) ? null : Args.Trim(),
            IsSystemAction ? null : BuildParametersJson(),
            IconRef));
    }

    private string BuildParametersJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var field in Fields)
            {
                switch (field.Type)
                {
                    case ParameterFieldType.Number:
                        if (double.TryParse(field.TextValue, out var number)) writer.WriteNumber(field.Key, number);
                        break;
                    case ParameterFieldType.Boolean:
                        writer.WriteBoolean(field.Key, field.BoolValue);
                        break;
                    case ParameterFieldType.Select:
                        if (field.SelectedOption is not null) writer.WriteString(field.Key, field.SelectedOption.Value);
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(field.TextValue)) writer.WriteString(field.Key, field.TextValue.Trim());
                        break;
                }
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public sealed record PluginOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    public sealed record ActionOption(string PluginId, string ActionId, string Name, string ParameterLabel, string? ParametersSchemaJson = null)
    {
        public override string ToString() => Name;
    }

    private sealed class SchemaDto
    {
        public List<SchemaFieldDto> Fields { get; set; } = [];
    }

    private sealed class SchemaFieldDto
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "text";
        public bool Required { get; set; }
        public bool Dynamic { get; set; }
    }
}
