namespace MobileDeck.Core;

// DTOs propios, no compartidos con Deck.Api.Dtos a propósito: referenciar
// Deck.Api desde acá arrastraría ASP.NET Core + EF Core + Sqlite al build de
// Android, mismo criterio que Clients/WebDeck tiene sus propios tipos en
// TypeScript en vez de generarlos del servidor.

public enum ButtonSlotType { Action, Folder }

public enum PluginState { Loaded, Initializing, Ready, Connecting, Connected, Disconnected, Faulted }

public sealed record ProfileDto(Guid Id, string Name, Guid RootPageId);

public sealed record PageDto(Guid Id, string Name, int Rows, int Columns, IReadOnlyList<ButtonSlotDto> Buttons);

public sealed record ButtonSlotDto(
    Guid Id, Guid PageId, int Row, int Column, ButtonSlotType Type,
    Guid? TargetPageId, string? Label, string? IconRef, IReadOnlyList<ActionStepDto> Steps);

public sealed record ActionStepDto(int Order, string PluginId, string ActionId, string ParametersJson);

public sealed record PluginDto(
    string Id, string Name, string Version, PluginState State, string? LastError, IReadOnlyList<PluginActionDto> Actions);

public sealed record PluginActionDto(string Id, string Name, string? Description);

public sealed record ExecuteButtonResult(
    bool Success, Guid? NavigatedToPageId, IReadOnlyList<ActionStepResultDto>? StepResults, string? Error);

public sealed record ActionStepResultDto(bool Success, string? Message);

public sealed record PluginEventMessage(string PluginId, string EventId, string PayloadJson, DateTimeOffset OccurredAt);
