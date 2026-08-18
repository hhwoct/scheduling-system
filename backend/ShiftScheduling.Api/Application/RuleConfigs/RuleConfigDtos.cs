namespace ShiftScheduling.Api.Application.RuleConfigs;

public sealed record RuleConfigItem(
    long Id,
    string RuleKey,
    string RuleName,
    string RuleValue,
    string ValueType,
    string? Remark,
    int Status,
    int Version);

/// <summary>Version 为 null 表示不启用乐观锁校验（兼容旧客户端）；提供时执行 compare-and-swap。</summary>
public sealed record RuleConfigUpdateRequest(string RuleValue, int Status, int? Version = null);
