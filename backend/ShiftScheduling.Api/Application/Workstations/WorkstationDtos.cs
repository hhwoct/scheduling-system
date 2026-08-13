namespace ShiftScheduling.Api.Application.Workstations;

public sealed record WorkstationItem(
    long Id,
    string Code,
    string Name,
    int SortOrder,
    int IsLowSkill,
    string? Remark,
    int Status);

public sealed record WorkstationUpdateRequest(
    string Name,
    string? Remark,
    int Status,
    int IsLowSkill = 0);

public sealed record WorkstationCreateRequest(
    string Code,
    string Name,
    int SortOrder,
    string? Remark,
    int IsLowSkill = 0);