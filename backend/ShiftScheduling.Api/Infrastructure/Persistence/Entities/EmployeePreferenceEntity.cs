namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

/// <summary>排班偏好学习统计：员工 × (班次/工作站/休息) × day_type 认可频次。</summary>
public sealed class EmployeePreferenceEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public long EmployeeId { get; set; }

    public string DayType { get; set; } = "WORKDAY";

    /// <summary>班次 code；休息日样本为 NULL。</summary>
    public string? ShiftCode { get; set; }

    /// <summary>工作站；休息日样本为 NULL。</summary>
    public long? WorkstationId { get; set; }

    public int Freq { get; set; }

    public DateTime LastSeenAt { get; set; }
}
