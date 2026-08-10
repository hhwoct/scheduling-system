namespace ShiftScheduling.Api.Application.Schedules;

public sealed record GenerateScheduleRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? PlanName = null);

public sealed record SchedulePlanItem(
    long Id,
    string PlanName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    long? CreatedBy,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    int EmployeeCount,
    int IssueCount);

public sealed record GenerateScheduleResult(
    long PlanId,
    string PlanName,
    int RestDayCount,
    int ShiftAssignmentCount,
    int WorkstationAssignmentCount,
    int SummaryCount,
    int IssueCount,
    IReadOnlyList<string> IssueTypes);

public sealed record MonthViewItem(
    long EmployeeId,
    string EmployeeNo,
    string EmployeeName,
    string Department,
    IReadOnlyList<MonthDayCell> Days);

public sealed record MonthDayCell(
    DateOnly WorkDate,
    int IsRestDay,
    string? ShiftCode,
    decimal WorkHours);

public sealed record WeekViewItem(
    long EmployeeId,
    string EmployeeNo,
    string EmployeeName,
    string Department,
    IReadOnlyList<WeekDayShift> Days);

public sealed record WeekDayShift(
    DateOnly WorkDate,
    int IsRestDay,
    string? ShiftCode,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    decimal WorkHours,
    string? CoveredWorkstations);

public sealed record DailyViewItem(
    DateOnly WorkDate,
    long EmployeeId,
    string EmployeeNo,
    string EmployeeName,
    string? EmployeePosition,
    long? ShiftTemplateId,
    string? ShiftCode,
    long? WorkstationId,
    string? WorkstationName,
    TimeSpan TimeSlot,
    int SkillScore);

public sealed record ScheduleSummaryDto(
    long PlanId,
    int EmployeeCount,
    int RestDayCount,
    int WorkDayCount,
    decimal TotalWorkHours,
    int GapCount,
    int WarnCount,
    int ErrorCount);

public sealed record AdjustScheduleItem(
    long EmployeeId,
    DateOnly WorkDate,
    long? ShiftTemplateId,
    long? WorkstationId,
    TimeSpan? TimeSlot);

public sealed record AdjustScheduleRequest(IReadOnlyList<AdjustScheduleItem> Items);
