namespace ShiftScheduling.Api.Algorithm;

public sealed record EmployeeInput(
    long Id,
    string EmployeeNo,
    string Name,
    string Department,
    string? PrimaryPosition,
    decimal MaxWeeklyHours);

public sealed record SkillInput(long EmployeeId, long WorkstationId, int SkillScore);

public sealed record DateParameterInput(
    DateOnly WorkDate,
    int WeekDay,
    string DayType,
    int IsLegalHoliday,
    int IsHolidayEve);

public sealed record ShiftTemplateInput(
    long Id,
    string Code,
    string Name,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int IsCrossDay,
    int Priority,
    IReadOnlyList<long> WorkstationIds);

public sealed record StaffingRequirementInput(
    string DayType,
    long WorkstationId,
    TimeSpan TimeSlot,
    int RequiredCount);

/// <summary>
/// 已批准请假：排班时该员工在 StartDate~EndDate 内强制视为休息日。
/// </summary>
public sealed record ApprovedLeaveInput(
    long EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record SchedulingInput(
    long StoreId,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<EmployeeInput> Employees,
    IReadOnlyList<SkillInput> Skills,
    IReadOnlyList<DateParameterInput> DateParameters,
    IReadOnlyList<ShiftTemplateInput> ShiftTemplates,
    IReadOnlyList<StaffingRequirementInput> StaffingRequirements,
    IReadOnlyList<ApprovedLeaveInput> ApprovedLeaves,
    IReadOnlyDictionary<long, bool> LowSkillWorkstationIds,
    int DefaultMonthlyRestDays,
    decimal MaxWeeklyHours,
    int MaxConsecutiveWorkDays,
    int MinRestHoursAfterNightShift);

public sealed record RestDayAssignment(long EmployeeId, DateOnly WorkDate);

public sealed record ShiftAssignment(
    long EmployeeId,
    DateOnly WorkDate,
    long ShiftTemplateId,
    string ShiftCode,
    long? WorkstationId = null);

public sealed record WorkstationAssignment(
    long EmployeeId,
    DateOnly WorkDate,
    TimeSpan TimeSlot,
    long WorkstationId,
    int SkillScore,
    long ShiftTemplateId);

public sealed record DaySummaryOutput(
    long EmployeeId,
    DateOnly WorkDate,
    int IsRestDay,
    long? ShiftTemplateId,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    decimal WorkHours,
    string? CoveredWorkstations);

public sealed record ScheduleIssueOutput(
    string IssueType,
    string Severity,
    DateOnly? WorkDate,
    TimeSpan? TimeSlot,
    long? EmployeeId,
    long? WorkstationId,
    string Description);

public sealed record SchedulingOutput(
    IReadOnlyList<RestDayAssignment> RestDays,
    IReadOnlyList<ShiftAssignment> ShiftAssignments,
    IReadOnlyList<WorkstationAssignment> WorkstationAssignments,
    IReadOnlyList<DaySummaryOutput> DaySummaries,
    IReadOnlyList<ScheduleIssueOutput> Issues);

public static class SchedulingTimeHelper
{
    public const int SlotsPerDay = 48;
    private static readonly TimeSpan DayEnd = TimeSpan.FromHours(24);

    public static IReadOnlyList<TimeSpan> GetShiftSlots(TimeSpan start, TimeSpan end, int isCrossDay)
    {
        var slots = new List<TimeSpan>();
        var current = start;

        if (isCrossDay == 1)
        {
            while (current < DayEnd)
            {
                slots.Add(current);
                current += TimeSpan.FromMinutes(30);
            }

            current = TimeSpan.Zero;
            while (current < end)
            {
                slots.Add(current);
                current += TimeSpan.FromMinutes(30);
            }
        }
        else
        {
            while (current < end)
            {
                slots.Add(current);
                current += TimeSpan.FromMinutes(30);
            }
        }

        return slots;
    }

    public static decimal GetShiftHours(TimeSpan start, TimeSpan end, int isCrossDay)
    {
        if (isCrossDay == 1)
        {
            return ((decimal)(DayEnd - start).TotalMinutes + (decimal)end.TotalMinutes) / 60m;
        }

        return (decimal)(end - start).TotalMinutes / 60m;
    }

    /// <summary>
    /// P1-5 修复2：修正跨天检测。
    /// 结束时间早于开始时间视为跨天；或开始时间在 20:00 后且结束时间在 06:00 前也视为跨天。
    /// </summary>
    public static bool IsCrossDay(TimeSpan start, TimeSpan end)
        => end < start
           || (start >= TimeSpan.FromHours(20) && end <= TimeSpan.FromHours(6));

    public static bool IsNightShift(ShiftTemplateInput shift)
        => shift.IsCrossDay == 1 || shift.EndTime >= TimeSpan.FromHours(20);
}
