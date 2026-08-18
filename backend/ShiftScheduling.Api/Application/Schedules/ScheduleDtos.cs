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
    IReadOnlyList<string> IssueTypes,
    decimal DemandMinHours,
    decimal DemandIdealHours,
    decimal CoveredHours,
    decimal GapHours,
    int CoveragePct,
    int DemandShiftCount);

public sealed record MonthViewItem(
    long EmployeeId,
    string EmployeeNo,
    string EmployeeName,
    string Department,
    int IsParttime,
    IReadOnlyList<MonthDayCell> Days);

public sealed record MonthDayCell(
    DateOnly WorkDate,
    int IsRestDay,
    string? ShiftCode,
    decimal WorkHours,
    TimeSpan? BreakStartTime,
    TimeSpan? BreakEndTime);

public sealed record WeekViewItem(
    long EmployeeId,
    string EmployeeNo,
    string EmployeeName,
    string Department,
    int IsParttime,
    IReadOnlyList<WeekDayShift> Days);

public sealed record WeekDayShift(
    DateOnly WorkDate,
    int IsRestDay,
    string? ShiftCode,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    decimal WorkHours,
    string? CoveredWorkstations,
    TimeSpan? BreakStartTime,
    TimeSpan? BreakEndTime,
    string? BreakCoverEmployeeName);

public sealed record DailyViewItem(
    DateOnly WorkDate,
    long EmployeeId,
    string EmployeeNo,
    string EmployeeName,
    string? EmployeePosition,
    int IsParttime,
    long? ShiftTemplateId,
    string? ShiftCode,
    long? WorkstationId,
    string? WorkstationName,
    TimeSpan TimeSlot,
    int SkillScore,
    TimeSpan? BreakStartTime,
    TimeSpan? BreakEndTime,
    string? BreakCoverEmployeeName);

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

/// <summary>
/// 拖动移动员工某天在某个工作站的连续工作段（时间平移 + 换工作站）。
/// FromTimeSlot/ToTimeSlot 为段起始时段的 HH:mm（30 分钟对齐）。
/// </summary>
public sealed record MoveScheduleSegmentRequest(
    long EmployeeId,
    DateOnly WorkDate,
    long FromWorkstationId,
    string FromTimeSlot,
    string ToTimeSlot,
    long ToWorkstationId);

public sealed record MoveScheduleSegmentResult(
    int MovedSlots,
    string FromTimeSlot,
    string ToTimeSlot,
    long ToWorkstationId);

/// <summary>
/// 设置员工某天的休息/上班状态（日明细点色块调整）。
/// IsRestDay=1 → 设为休息（删除当天明细）；IsRestDay=0 → 设为上班（需班次，工作站按技能自动选择）。
/// </summary>
public sealed record SetDayStatusItem(
    long EmployeeId,
    DateOnly WorkDate,
    int IsRestDay,
    long? ShiftTemplateId = null,
    long? WorkstationId = null);

public sealed record SetDayStatusRequest(IReadOnlyList<SetDayStatusItem> Items);

/// <summary>
/// 调整员工某天某个半小时时段的状态（甘特图/日明细点色块）。
/// IsRest=1 → 该半小时改为休息（作为班中休息记录在该时段）；
/// IsRest=0 → 该半小时恢复上班（清除该时段休息标记）。
/// </summary>
public sealed record SetSlotStatusItem(
    long EmployeeId,
    DateOnly WorkDate,
    TimeSpan TimeSlot,
    int IsRest);

public sealed record SetSlotStatusRequest(IReadOnlyList<SetSlotStatusItem> Items);
