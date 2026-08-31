using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>算法单元测试共用的输入构造器。</summary>
public static class AlgorithmTestData
{
    public static EmployeeInput Employee(long id, string no, string name, string department, string? position = "岗位", decimal maxWeeklyHours = 48, int isParttime = 0)
        => new(id, no, name, department, position, maxWeeklyHours, isParttime);

    public static SkillInput Skill(long employeeId, long workstationId, int score = 5, int isPrimary = 1)
        => new(employeeId, workstationId, score, isPrimary);

    public static DateParameterInput Day(DateOnly date, string dayType = "WORKDAY", int isLegalHoliday = 0, int isHolidayEve = 0)
        // 审查修复：WeekDay 必须与生产口径一致（MySQL DAYOFWEEK：1=周日 … 7=周六）。
        // 原实现 ((int)DayOfWeek + 6) % 7 + 1 产生反向映射（周日=7），导致周工时
        // 周一重置（WeekDay==2）与高峰日判断（6/7=周五/六）在测试数据中落在错误日期。
        => new(date, (int)date.DayOfWeek + 1, dayType, isLegalHoliday, isHolidayEve);

    public static List<DateParameterInput> Days(DateOnly start, int count, string dayType = "WORKDAY")
    {
        var list = new List<DateParameterInput>();
        for (var i = 0; i < count; i++)
        {
            list.Add(Day(start.AddDays(i), dayType));
        }
        return list;
    }

    public static ShiftTemplateInput Shift(
        long id,
        string code,
        string name,
        TimeSpan start,
        TimeSpan end,
        int isCrossDay,
        int priority,
        params long[] workstationIds)
        => new(id, code, name, start, end, isCrossDay, priority, workstationIds.ToList());

    public static StaffingRequirementInput Req(string dayType, long workstationId, TimeSpan slot, int required, int ideal = -1)
        => new(dayType, workstationId, slot, required, ideal);

    /// <summary>为班次覆盖的全部时段生成人数需求。</summary>
    public static List<StaffingRequirementInput> ReqsForShift(
        string dayType,
        ShiftTemplateInput shift,
        int count,
        long? workstationId = null)
    {
        var ws = workstationId ?? shift.WorkstationIds[0];
        return SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay)
            .Select(slot => Req(dayType, ws, slot, count))
            .ToList();
    }

    public static SchedulingInput Build(
        List<EmployeeInput> employees,
        List<SkillInput> skills,
        List<DateParameterInput> dates,
        List<ShiftTemplateInput> shifts,
        List<StaffingRequirementInput> requirements,
        int defaultMonthlyRestDays = 4,
        int maxConsecutiveWorkDays = 6,
        int minRestHoursAfterNightShift = 10,
        decimal minDailyWorkHours = 0m)
        => new(
            1,
            dates.Count == 0 ? new DateOnly(2026, 1, 1) : dates.Min(d => d.WorkDate),
            dates.Count == 0 ? new DateOnly(2026, 1, 1) : dates.Max(d => d.WorkDate),
            employees,
            skills,
            dates,
            shifts,
            requirements,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput>(),
            new Dictionary<long, bool>(),
            new Dictionary<long, string>(),
            defaultMonthlyRestDays,
            maxConsecutiveWorkDays,
            minRestHoursAfterNightShift,
            minDailyWorkHours);
}