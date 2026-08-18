namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class StaffingRequirementEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string DayType { get; set; } = string.Empty;

    public long WorkstationId { get; set; }

    public TimeSpan TimeSlot { get; set; }

    /// <summary>最少人数（硬性要求，低于即缺口）。</summary>
    public int RequiredCount { get; set; }

    /// <summary>最好人数（软性目标，尽量达到但不超过硬约束）。</summary>
    public int IdealCount { get; set; }

    /// <summary>该格备注（如「周五要加人」），可空。</summary>
    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
