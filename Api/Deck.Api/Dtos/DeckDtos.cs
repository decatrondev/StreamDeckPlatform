using Deck.Core.Model;
using Deck.Core.Plugins;

namespace Deck.Api.Dtos;

public sealed record ProfileDto(Guid Id, string Name, Guid RootPageId)
{
    public static ProfileDto From(Profile p) => new(p.Id, p.Name, p.RootPageId);
}

public sealed record CreateProfileRequest(string Name, Guid RootPageId);

public sealed record PageDto(Guid Id, string Name, int Rows, int Columns, IReadOnlyList<ButtonSlotDto> Buttons)
{
    public static PageDto From(Page p, IReadOnlyList<ButtonSlotDto> buttons) =>
        new(p.Id, p.Name, p.Rows, p.Columns, buttons);
}

public sealed record CreatePageRequest(string Name, int Rows, int Columns);

public sealed record ButtonSlotDto(
    Guid Id, Guid PageId, int Row, int Column, ButtonSlotType Type,
    Guid? TargetPageId, string? Label, string? IconRef, IReadOnlyList<ActionStepDto> Steps)
{
    public static ButtonSlotDto From(ButtonSlot b, IReadOnlyList<ActionStepDto> steps) =>
        new(b.Id, b.PageId, b.Row, b.Column, b.Type, b.TargetPageId, b.Label, b.IconRef, steps);
}

public sealed record ActionStepDto(int Order, string PluginId, string ActionId, string ParametersJson)
{
    public static ActionStepDto From(ActionStep s) => new(s.Order, s.PluginId, s.ActionId, s.ParametersJson);
}

// Upsert completo de un botón: folder XOR lista de pasos, según Type — la API
// valida esa exclusión antes de tocar la base (ver ButtonSlotsController).
public sealed record UpsertButtonSlotRequest(
    int Row, int Column, ButtonSlotType Type,
    Guid? TargetPageId, string? Label, string? IconRef, IReadOnlyList<ActionStepDto>? Steps);

public sealed record PluginDto(string Id, string Name, string Version, PluginState State, string? LastError, IReadOnlyList<PluginActionDto> Actions)
{
    public static PluginDto From(LoadedPlugin p) => new(
        p.Metadata.Id, p.Metadata.Name, p.Metadata.Version, p.State, p.LastError,
        p.Instance.Actions.Select(a => new PluginActionDto(a.Id, a.Name, a.Description)).ToList());
}

public sealed record PluginActionDto(string Id, string Name, string? Description);

public sealed record ExecuteButtonResult(bool Success, Guid? NavigatedToPageId, IReadOnlyList<ActionStepResultDto>? StepResults, string? Error);

public sealed record ActionStepResultDto(bool Success, string? Message);

public sealed record PluginEventMessage(string PluginId, string EventId, string PayloadJson, DateTimeOffset OccurredAt);
