namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class ScheduleSummaryEntity
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long StoreId { get; set; }

    public long EmployeeId { get; set; }

    public DateOnly WorkDate { get; set; }

    public int IsRestDay { get; set; }

    public long? ShiftTemplateId { get; set; }

    public TimeSpan? StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }

    public decimal WorkHours { get; set; }

    public string? CoveredWorkstations { get; set; }

    /// <summary>班中休息开始时间（每次固定 30 分钟，跨天班可为 23:30 等）。</summary>
    public TimeSpan? BreakStartTime { get; set; }

    /// <summary>班中休息结束时间。</summary>
    public TimeSpan? BreakEndTime { get; set; }

    /// <summary>顶岗员工 ID；null 表示无人顶岗（对应 BREAK_UNCOVERED 告警）。</summary>
    public long? BreakCoverEmployeeId { get; set; }

    /// <summary>休息时该员工所在的工作站（主工作站），用于合理度扣减与告警定位。</summary>
    public long? BreakWorkstationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
