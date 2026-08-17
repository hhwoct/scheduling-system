namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// 门店高峰禁休时段（班中休息不得与其重叠，如 20:00-22:00）。
/// admin 端可增删改查，按门店配置。
/// </summary>
public sealed class PeakRestrictedHourEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public int Status { get; set; } = 1;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
