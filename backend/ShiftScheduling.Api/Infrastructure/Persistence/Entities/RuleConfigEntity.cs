namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class RuleConfigEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string RuleKey { get; set; } = string.Empty;

    public string RuleName { get; set; } = string.Empty;

    public string RuleValue { get; set; } = string.Empty;

    public string ValueType { get; set; } = "number";

    public string? Remark { get; set; }

    public int Status { get; set; }

    /// <summary>
    /// 乐观锁版本号。更新时检查版本，防止并发覆盖。
    /// </summary>
    public int Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
