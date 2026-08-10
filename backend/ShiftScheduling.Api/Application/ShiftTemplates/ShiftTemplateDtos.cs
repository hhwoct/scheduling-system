namespace ShiftScheduling.Api.Application.ShiftTemplates;

public sealed record ShiftTemplateItem(
    long Id,
    string Code,
    string Name,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int IsCrossDay,
    int Priority,
    int Status,
    IReadOnlyList<string> CoveredWorkstations);

public sealed record ShiftTemplateUpdateRequest(
    string Name,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int IsCrossDay,
    int Priority,
    int Status);
