namespace ShiftScheduling.Api.Algorithm;

public sealed record EmployeeInput(
    long Id,
    string EmployeeNo,
    string Name,
    string Department,
    string? PrimaryPosition,
    decimal MaxWeeklyHours,
    int IsParttime = 0);

public sealed record SkillInput(long EmployeeId, long WorkstationId, int SkillScore, int IsPrimarySkill = 0);

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

/// <summary>
/// 人数需求输入。RequiredCount = 最少人数（硬性，低于即缺口）；
/// IdealCount = 最好人数（软性，尽量达到）。IdealCount &lt;= 0 表示与 RequiredCount 相同。
/// </summary>
public sealed record StaffingRequirementInput(
    string DayType,
    long WorkstationId,
    TimeSpan TimeSlot,
    int RequiredCount,
    int IdealCount = -1);

/// <summary>
/// 已批准请假：排班时该员工在 StartDate~EndDate 内强制视为休息日。
/// </summary>
public sealed record ApprovedLeaveInput(
    long EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// 高峰禁休时段（班中休息不得与其重叠）。StartTime 含，EndTime 不含。
/// </summary>
public sealed record PeakRestrictedHourInput(
    TimeSpan StartTime,
    TimeSpan EndTime);

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
    IReadOnlyList<PeakRestrictedHourInput> PeakRestrictedHours,
    IReadOnlyDictionary<long, bool> LowSkillWorkstationIds,
    IReadOnlyDictionary<long, string> WarnOnlyGapWorkstations,
    int DefaultMonthlyRestDays,
    decimal MaxWeeklyHours,
    int MaxConsecutiveWorkDays,
    int MinRestHoursAfterNightShift,
    decimal MinDailyWorkHours,
    IReadOnlyDictionary<(long EmployeeId, string DayType), IReadOnlyDictionary<string, int>>? PreferenceShiftScores = null,
    IReadOnlyDictionary<(long EmployeeId, string DayType), IReadOnlyDictionary<long, int>>? PreferenceWsScores = null,
    decimal PreferenceWeight = 0m);

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

/// <summary>
/// 班中休息分配结果（每次固定 30 分钟）。
/// CoverEmployeeId 为 null 表示无人顶岗（对应 BREAK_UNCOVERED 告警）；
/// CoverInexperienced 表示顶岗人非该站主技能（对应 BREAK_BORROW_INEXPERIENCED 提示）。
/// </summary>
public sealed record BreakAssignment(
    long EmployeeId,
    DateOnly WorkDate,
    TimeSpan BreakStartTime,
    long? CoverEmployeeId,
    bool CoverInexperienced,
    long? WorkstationId);

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

/// <summary>
/// 需求覆盖统计（按营业日口径：某天凌晨时段按前一天类型取需求）。
/// DemandMinHours = 最少总需求人·时；DemandIdealHours = 最好总需求人·时；
/// CoveredHours = 工作站分配实际覆盖的需求人·时（按最少人数截断）；
/// GapHours = 最少人数未覆盖的缺口人·时。
/// </summary>
public sealed record DemandCoverageStats(
    decimal DemandMinHours,
    decimal DemandIdealHours,
    decimal CoveredHours,
    decimal GapHours,
    int CoveragePct,
    int DemandShiftCount);

public sealed record SchedulingOutput(
    IReadOnlyList<RestDayAssignment> RestDays,
    IReadOnlyList<ShiftAssignment> ShiftAssignments,
    IReadOnlyList<WorkstationAssignment> WorkstationAssignments,
    IReadOnlyList<BreakAssignment> BreakAssignments,
    IReadOnlyList<DaySummaryOutput> DaySummaries,
    IReadOnlyList<ScheduleIssueOutput> Issues,
    DemandCoverageStats DemandCoverage);

/// <summary>
/// 偏好学习评分（feature/schedule-pref-learning）。
/// 两个维度：班次偏好（店长习惯让员工上哪个班次）与工作站偏好（习惯在哪站）。
/// 权重 ≤ 0（规则 preference_weight=0）或未加载偏好时返回 0 = 关闭。
/// 作为分配排序的次级键：仅技能分相同/接近时贴合店长历史习惯，不改变硬约束。
/// </summary>
public static class PreferenceScoring
{
    public static int ForShift(
        long employeeId,
        ShiftTemplateInput shift,
        string dayType,
        SchedulingInput input)
    {
        if (input.PreferenceWeight <= 0m || input.PreferenceShiftScores is null)
        {
            return 0;
        }

        if (!input.PreferenceShiftScores.TryGetValue((employeeId, dayType), out var scores))
        {
            return 0;
        }

        return Math.Min(scores.GetValueOrDefault(shift.Code), 10);
    }

    public static int ForWorkstation(long employeeId, long workstationId, string dayType, SchedulingInput input)
    {
        if (input.PreferenceWeight <= 0m || input.PreferenceWsScores is null)
        {
            return 0;
        }

        if (!input.PreferenceWsScores.TryGetValue((employeeId, dayType), out var scores))
        {
            return 0;
        }

        return Math.Min(scores.GetValueOrDefault(workstationId), 10);
    }
}

public static class SchedulingTimeHelper
{
    public const int SlotsPerDay = 48;
    private static readonly TimeSpan DayEnd = TimeSpan.FromHours(24);

    public static IReadOnlyList<TimeSpan> GetShiftSlots(TimeSpan start, TimeSpan end, int isCrossDay)
    {
        var slots = new List<TimeSpan>();

        if (isCrossDay == 1)
        {
            // 归一化：结束时间 24:00 表示当日结束边界（不再当作完整额外一天）；
            // 大于 24:00（如 26:00）按次日 02:00 理解。
            var nextDayEnd = end >= DayEnd ? end - DayEnd : end;
            var current = start;
            while (current < DayEnd)
            {
                slots.Add(current);
                current += TimeSpan.FromMinutes(30);
            }

            current = TimeSpan.Zero;
            while (current < nextDayEnd)
            {
                slots.Add(current);
                current += TimeSpan.FromMinutes(30);
            }
        }
        else
        {
            var endLimit = end >= DayEnd ? DayEnd : end;
            var current = start;
            while (current < endLimit)
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
            // 归一化：24:00 表示当日结束边界，不再计入完整额外一天
            var nextDayEnd = end >= DayEnd ? end - DayEnd : end;
            return ((decimal)(DayEnd - start).TotalMinutes + (decimal)nextDayEnd.TotalMinutes) / 60m;
        }

        var endLimit = end >= DayEnd ? DayEnd : end;
        return (decimal)(endLimit - start).TotalMinutes / 60m;
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

    /// <summary>
    /// 班次覆盖时段的日历日期：跨天班次中，早于班次开始时刻的时段（午夜回绕部分）属于次日。
    /// 例如 22:00-02:00 的班次在 8/10 开始，其 00:00-01:30 时段的日历日期为 8/11。
    /// </summary>
    public static DateOnly SlotCalendarDate(TimeSpan slot, TimeSpan shiftStart, DateOnly shiftDate)
        => slot < shiftStart ? shiftDate.AddDays(1) : shiftDate;
}
