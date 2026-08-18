namespace ShiftScheduling.Api.Application.StaffingRequirements;

/// <summary>
/// 人数需求条目（返回端）；TimeSlot 为 30 分钟对齐的时段起点（00:00-23:30）。
/// RequiredCount = 最少人数（硬性），IdealCount = 最好人数（软性，IdealCount >= RequiredCount）。
/// </summary>
public sealed record StaffingRequirementItem(
    long Id,
    string DayType,
    long WorkstationId,
    string WorkstationCode,
    string WorkstationName,
    TimeSpan TimeSlot,
    int RequiredCount,
    int IdealCount,
    string? Remark);

/// <summary>批量保存某个日期类型全部时段的人数需求；未提交的工作站时段视为 0。</summary>
public sealed record StaffingRequirementSaveRequest(
    string DayType,
    List<StaffingRequirementEntry> Entries);

/// <summary>
/// 单个工作站时段的保存项；TimeSlot 格式 HH:mm 或 HH:mm:ss（必须 30 分钟对齐）。
/// RequiredCount = 最少人数；IdealCount = 最好人数（0 或缺省时视为与 RequiredCount 相同，且自动 >= RequiredCount）。
/// </summary>
public sealed record StaffingRequirementEntry(
    long WorkstationId,
    string TimeSlot,
    int RequiredCount,
    int IdealCount = 0,
    string? Remark = null);

/// <summary>批量保存结果。</summary>
public sealed record StaffingRequirementSaveResult(
    string DayType,
    int TotalSlots,
    int NonZeroSlots);

/// <summary>支持配置的日期类型（与 date_parameters.day_type、算法匹配键一致）。</summary>
public static class StaffingDayTypes
{
    public const string Workday = "WORKDAY";
    public const string Weekend = "WEEKEND";
    public const string Holiday = "HOLIDAY";

    public static readonly string[] All = [Workday, Weekend, Holiday];

    public static bool IsValid(string? dayType) =>
        dayType is not null && All.Contains(dayType);
}
