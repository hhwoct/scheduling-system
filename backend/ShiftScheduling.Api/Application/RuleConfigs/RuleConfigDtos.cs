namespace ShiftScheduling.Api.Application.RuleConfigs;

public sealed record RuleConfigItem(
    long Id,
    string RuleKey,
    string RuleName,
    string RuleValue,
    string ValueType,
    string? Remark,
    int Status);

public sealed record RuleConfigUpdateRequest(string RuleValue, int Status);
