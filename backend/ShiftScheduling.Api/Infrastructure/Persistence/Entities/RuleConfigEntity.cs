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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
