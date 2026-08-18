using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deck.Core.Model;
using Deck.Core.SystemActions;
using Deck.SDK.Plugins;
using Deck.UI.Avalonia.Services;
using Microsoft.EntityFrameworkCore;

namespace Deck.UI.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly DeckAppService _app;
    private readonly List<Page> _breadcrumb = [];

    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<ButtonSlotViewModel> Buttons { get; } = [];
    public ObservableCollection<string> BreadcrumbNames { get; } = [];

    [ObservableProperty]
    public partial Profile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial Page? CurrentPage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsDialogOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsPageOpen { get; set; }

    [ObservableProperty]
    public partial AssignActionDialogViewModel? Dialog { get; set; }

    [ObservableProperty]
    public partial SettingsPageViewModel? Settings { get; set; }

    public bool CanNavigateBack => _breadcrumb.Count > 1;

    public IRelayCommand NavigateBackCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }

    // El diseñador de Avalonia instancia esto sin argumentos — solo para
    // previsualización, nunca corre en runtime real (ver App.axaml.cs).
    public MainViewModel() : this(null!) { }

    public MainViewModel(DeckAppService app)
    {
        _app = app;
        NavigateBackCommand = new AsyncRelayCommand(NavigateBackAsync, () => CanNavigateBack);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
    }

    private void OpenSettings()
    {
        var settings = new SettingsPageViewModel(_app);
        settings.Closed += () =>
        {
            IsSettingsPageOpen = false;
            Settings = null;
        };

        Settings = settings;
        IsSettingsPageOpen = true;
    }

    public async Task InitializeAsync()
    {
        var profiles = await _app.Db.Profiles.ToListAsync();
        foreach (var profile in profiles) Profiles.Add(profile);

        if (profiles.Count > 0) await SelectProfileAsync(profiles[0]);
    }

    // Llamado desde el code-behind (ListBox.SelectionChanged) — la selección
    // dispara una carga async, y Avalonia no tiene binding async nativo para
    // SelectedItem.
    public async Task SelectProfileAsync(Profile profile)
    {
        SelectedProfile = profile;
        _breadcrumb.Clear();

        var rootPage = await _app.Db.Pages.SingleAsync(p => p.Id == profile.RootPageId);
        _breadcrumb.Add(rootPage);

        await LoadCurrentPageAsync();
    }

    private async Task NavigateIntoFolderAsync(Guid targetPageId)
    {
        var page = await _app.Db.Pages.SingleAsync(p => p.Id == targetPageId);
        _breadcrumb.Add(page);
        await LoadCurrentPageAsync();
    }

    private async Task NavigateBackAsync()
    {
        if (!CanNavigateBack) return;
        _breadcrumb.RemoveAt(_breadcrumb.Count - 1);
        await LoadCurrentPageAsync();
    }

    private async Task LoadCurrentPageAsync()
    {
        var page = _breadcrumb[^1];
        CurrentPage = page;

        BreadcrumbNames.Clear();
        foreach (var p in _breadcrumb) BreadcrumbNames.Add(p.Name);
        NavigateBackCommand.NotifyCanExecuteChanged();

        var slots = await _app.Db.ButtonSlots
            .Where(b => b.PageId == page.Id)
            .ToListAsync();

        foreach (var old in Buttons) old.Activated -= OnButtonActivatedAsync;
        Buttons.Clear();

        for (var row = 0; row < page.Rows; row++)
        {
            for (var col = 0; col < page.Columns; col++)
            {
                var slot = slots.FirstOrDefault(s => s.Row == row && s.Column == col);
                var vm = new ButtonSlotViewModel(row, col, slot, _app.Icons);
                vm.Activated += OnButtonActivatedAsync;
                vm.EditRequested += OpenAssignDialogAsync;
                vm.ClearRequested += OnButtonClearRequestedAsync;
                Buttons.Add(vm);
            }
        }
    }

    // Vacía una tecla ya asignada (y, si era carpeta, deja la página hija
    // huérfana sin borrarla — perder el contenido de una carpeta sin avisar
    // sería peor que dejar basura desreferenciada).
    private async Task OnButtonClearRequestedAsync(ButtonSlotViewModel button)
    {
        if (button.Slot is null) return;

        var steps = await _app.Db.ActionSteps
            .Where(a => a.ButtonSlotId == button.Slot.Id)
            .ToListAsync();
        _app.Db.ActionSteps.RemoveRange(steps);
        _app.Db.ButtonSlots.Remove(button.Slot);
        await _app.Db.SaveChangesAsync();

        button.Apply(null);
    }

    private async Task OnButtonActivatedAsync(ButtonSlotViewModel button)
    {
        if (button.Slot is null)
        {
            await OpenAssignDialogAsync(button);
            return;
        }

        if (button.Slot.Type == ButtonSlotType.Folder)
        {
            await NavigateIntoFolderAsync(button.Slot.TargetPageId!.Value);
            return;
        }

        button.IsRunning = true;
        StatusMessage = null;

        var steps = await _app.Db.ActionSteps
            .Where(a => a.ButtonSlotId == button.Slot.Id)
            .ToListAsync();

        var result = await _app.Executor.RunAsync(steps);
        button.IsRunning = false;

        StatusMessage = result.Success
            ? $"✓ {button.Label}: {result.StepResults[^1].Message}"
            : $"✗ {button.Label}: {result.StepResults[result.FailedAtStep!.Value].Message}";
    }

    private async Task OpenAssignDialogAsync(ButtonSlotViewModel button)
    {
        var pluginActions = _app.Plugins.Plugins
            .Where(p => p.Metadata.Id != SystemActionsPlugin.PluginId)
            .SelectMany(p => p.Instance.Actions.Select(a => (p.Metadata.Id, p.Metadata.Name, a)))
            .ToList();

        var existing = button.Slot is null ? null : await LoadExistingAssignmentAsync(button.Slot);

        var dialog = new AssignActionDialogViewModel(pluginActions, _app.Icons, ResolveParameterOptionsAsync, existing);
        dialog.Closed += async result => await OnDialogClosedAsync(button, result);

        Dialog = dialog;
        IsDialogOpen = true;
    }

    // Le pasa al diálogo cómo resolver las opciones en vivo de un campo
    // "select" dinámico (ej. las escenas reales de OBS) — mismo patrón de
    // acceso a instancias que ya usa SettingsPageViewModel.GetInstance<T>.
    private Task<IReadOnlyList<ParameterOption>> ResolveParameterOptionsAsync(
        string pluginId, string actionId, string parameterKey, CancellationToken ct)
    {
        var plugin = _app.Plugins.Plugins.FirstOrDefault(p => p.Metadata.Id == pluginId)?.Instance;
        return plugin?.GetParameterOptionsAsync(actionId, parameterKey, ct)
            ?? Task.FromResult<IReadOnlyList<ParameterOption>>([]);
    }

    // Sin esto, click derecho → Editar abría el diálogo siempre vacío aunque
    // la tecla ya tuviera algo asignado — bug real, no solo falta de UX.
    private async Task<ExistingAssignment> LoadExistingAssignmentAsync(ButtonSlot slot)
    {
        if (slot.Type == ButtonSlotType.Folder)
        {
            return new ExistingAssignment(AssignMode.Folder, null, null, slot.Label ?? "", null, null, null, slot.IconRef);
        }

        var step = await _app.Db.ActionSteps
            .Where(a => a.ButtonSlotId == slot.Id)
            .OrderBy(a => a.Order)
            .FirstOrDefaultAsync();

        if (step is null)
        {
            return new ExistingAssignment(AssignMode.Action, null, null, slot.Label ?? "", null, null, null, slot.IconRef);
        }

        var isSystemAction = step.PluginId == SystemActionsPlugin.PluginId;
        if (!isSystemAction)
        {
            return new ExistingAssignment(
                AssignMode.Action, step.PluginId, step.ActionId, slot.Label ?? "", null, null, step.ParametersJson, slot.IconRef);
        }

        var parameters = JsonDocument.Parse(step.ParametersJson).RootElement;
        var pathOrUrl = step.ActionId == "open-url"
            ? parameters.GetProperty("url").GetString()
            : parameters.TryGetProperty("path", out var p) ? p.GetString() : null;
        var args = parameters.TryGetProperty("args", out var a) ? a.GetString() : null;

        return new ExistingAssignment(
            AssignMode.Action, step.PluginId, step.ActionId, slot.Label ?? "", pathOrUrl, args, null, slot.IconRef);
    }

    private async Task OnDialogClosedAsync(ButtonSlotViewModel button, AssignActionResult? result)
    {
        IsDialogOpen = false;
        Dialog = null;

        if (result is null || CurrentPage is null) return;

        // Reasignar una tecla que ya tenía algo: se borra lo viejo antes de
        // crear lo nuevo, para no dejar una fila huérfana en la misma
        // posición (Row, Column).
        if (button.Slot is not null)
        {
            var oldSteps = await _app.Db.ActionSteps
                .Where(a => a.ButtonSlotId == button.Slot.Id)
                .ToListAsync();
            _app.Db.ActionSteps.RemoveRange(oldSteps);
            _app.Db.ButtonSlots.Remove(button.Slot);
        }

        if (result.Mode == AssignMode.Folder)
        {
            var newPage = new Page { Id = Guid.NewGuid(), Name = result.Label, Rows = CurrentPage.Rows, Columns = CurrentPage.Columns };
            var slot = new ButtonSlot
            {
                Id = Guid.NewGuid(),
                PageId = CurrentPage.Id,
                Row = button.Row,
                Column = button.Column,
                Type = ButtonSlotType.Folder,
                TargetPageId = newPage.Id,
                Label = result.Label,
                IconRef = result.IconRef
            };

            _app.Db.Pages.Add(newPage);
            _app.Db.ButtonSlots.Add(slot);
            await _app.Db.SaveChangesAsync();

            button.Apply(slot);
        }
        else
        {
            var slot = new ButtonSlot
            {
                Id = Guid.NewGuid(),
                PageId = CurrentPage.Id,
                Row = button.Row,
                Column = button.Column,
                Type = ButtonSlotType.Action,
                Label = result.Label,
                IconRef = result.IconRef
            };

            var isSystemAction = result.PluginId == SystemActionsPlugin.PluginId;
            var parameters = !isSystemAction
                ? (result.ParametersJson ?? "{}")
                : result.ActionId == "open-url"
                    ? JsonSerializer.Serialize(new { url = result.PathOrUrl })
                    : JsonSerializer.Serialize(new { path = result.PathOrUrl, args = result.Args });

            var step = new ActionStep
            {
                Id = Guid.NewGuid(),
                ButtonSlotId = slot.Id,
                Order = 0,
                PluginId = result.PluginId!,
                ActionId = result.ActionId!,
                ParametersJson = parameters
            };

            _app.Db.ButtonSlots.Add(slot);
            _app.Db.ActionSteps.Add(step);
            await _app.Db.SaveChangesAsync();

            button.Apply(slot);
        }
    }
}
