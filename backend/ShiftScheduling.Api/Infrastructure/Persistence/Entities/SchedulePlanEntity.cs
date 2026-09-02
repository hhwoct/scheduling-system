namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class SchedulePlanEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = "DRAFT";

    /// <summary>计划来源：ALGO=算法生成（默认）；REAL=外部导入的真实班表，仅作为算法对比基线，不参与生成冲突判定。</summary>
    public string Source { get; set; } = "ALGO";

    public long? CreatedBy { get; set; }

    public DateTime? PublishedAt { get; set; }

    /// <summary>生成时日汇总快照（JSON，用于发布时计算店长手动调整量）。</summary>
    public string? GeneratedSummarySnapshot { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
