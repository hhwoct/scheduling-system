namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

/// <summary>排班调整明细：店长每次手动调整的结构化记录（纠错信号）。</summary>
public sealed class ScheduleAdjustmentEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public long PlanId { get; set; }

    public long? EmployeeId { get; set; }

    public DateOnly? WorkDate { get; set; }

    public TimeSpan? TimeSlot { get; set; }

    /// <summary>MOVE_SEGMENT / SET_REST / SET_WORK / CHANGE_SHIFT / CHANGE_WS。</summary>
    public string ActionType { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public long? OperatorUserId { get; set; }

    public string? OperatorName { get; set; }

    public DateTime CreatedAt { get; set; }
}
