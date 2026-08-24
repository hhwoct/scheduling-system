namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

/// <summary>偏好学习趋势：每期已发布计划的贴合率/覆盖率（仪表盘用）。</summary>
public sealed class PreferenceTrendEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public long PlanId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; }

    public decimal AdherencePct { get; set; }

    public decimal CoveragePct { get; set; }

    public int SampleDays { get; set; }

    /// <summary>店长手动调整条数（员工×日期 维度，发布时对比生成快照计算）。</summary>
    public int Adjustments { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
