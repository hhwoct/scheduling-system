namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class WorkstationEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>是否低技术含量岗位：缺口可建议找兼职临时填补。</summary>
    public int IsLowSkill { get; set; }

    public string? Remark { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}