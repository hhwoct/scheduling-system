namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class EmployeeEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string Department { get; set; } = string.Empty;

    public DateTime? HireDate { get; set; }

    public string? PrimaryPosition { get; set; }

    public decimal MaxWeeklyHours { get; set; } = 48;

    /// <summary>周工时上限是否跟随全局规则（1=跟随默认，0=个人自定义）。</summary>
    public int WeeklyHoursFollowDefault { get; set; } = 1;

    public int Status { get; set; }

    public int IsParttime { get; set; }

    /// <summary>是否通岗：大部分楼面工作都能做（传送/保洁/咨客/服务等低技能岗位）。</summary>
    public int IsGeneralist { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}