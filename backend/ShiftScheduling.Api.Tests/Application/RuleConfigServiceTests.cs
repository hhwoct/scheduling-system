using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.RuleConfigs;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>规则配置服务单元测试（含乐观锁版本冲突）。</summary>
public sealed class RuleConfigServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private RuleConfigService CreateService()
        => new(_factory.CreateDbContext(), _audit);

    private static async Task<RuleConfigEntity> SeedRuleAsync(TestDbContextFactory factory, string key = "max_weekly_hours", string value = "48", string valueType = "number")
    {
        var db = factory.CreateDbContext();
        var rule = new RuleConfigEntity
        {
            StoreId = 1, RuleKey = key, RuleName = "周工时上限", RuleValue = value, ValueType = valueType,
            Status = 1, Version = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.RuleConfigs.Add(rule);
        await db.SaveChangesAsync();
        return rule;
    }

    [Fact]
    public async Task ListAllAsync_ReturnsStoreRules()
    {
        await SeedRuleAsync(_factory);
        var service = CreateService();
        var list = await service.ListAllAsync(1, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("max_weekly_hours", list[0].RuleKey);
    }

    [Fact]
    public async Task UpdateAsync_EmptyValue_Throws()
    {
        await SeedRuleAsync(_factory);
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(1, new RuleConfigUpdateRequest("", 1), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_RULE_VALUE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_NumberRuleWithNonNumeric_Throws()
    {
        await SeedRuleAsync(_factory);
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(1, new RuleConfigUpdateRequest("abc", 1), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_RULE_VALUE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_NegativeNumber_Throws()
    {
        await SeedRuleAsync(_factory);
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(1, new RuleConfigUpdateRequest("-5", 1), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_RULE_VALUE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_StaleVersion_ThrowsConflict()
    {
        await SeedRuleAsync(_factory); // Version = 1
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(1, new RuleConfigUpdateRequest("50", 1, Version: 0), 1, 9, "a", CancellationToken.None));
        Assert.Equal("RULE_VERSION_CONFLICT", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_MatchingVersion_UpdatesAndIncrementsVersion()
    {
        var rule = await SeedRuleAsync(_factory);
        var service = CreateService();
        var result = await service.UpdateAsync(
            rule.Id,
            new RuleConfigUpdateRequest("50", 1, Version: rule.Version),
            1, 9, "管理员", CancellationToken.None);

        Assert.Equal("50", result.RuleValue);
        Assert.Equal(rule.Version + 1, result.Version);
        Assert.Contains(_audit.Entries, e => e.ActionType == "UPDATE_RULE_CONFIG");
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(999, new RuleConfigUpdateRequest("50", 1), 1, 9, "a", CancellationToken.None));
    }
}
