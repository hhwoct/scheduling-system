namespace ShiftScheduling.Api.Application.Preferences;

public sealed record PreferenceStats(
    int LearnedPeriods,
    int SampleDays,
    decimal CoveragePct,
    decimal AdherencePct,
    int PreferenceRows,
    decimal Weight);

public sealed record PreferenceMatrixItem(
    string EmployeeNo,
    string EmployeeName,
    string WorkstationCode,
    string DayType,
    int Freq);

/// <summary>Top 偏好：班次偏好（ShiftCode 非空）或休息偏好（两者皆 NULL）。</summary>
public sealed record PreferenceTopItem(
    string EmployeeNo,
    string EmployeeName,
    string DayType,
    string? ShiftCode,
    int Freq);

public sealed record PreferenceTrendItem(
    long PlanId,
    string PlanName,
    DateTime PublishedAt,
    decimal AdherencePct,
    decimal CoveragePct,
    int SampleDays);
