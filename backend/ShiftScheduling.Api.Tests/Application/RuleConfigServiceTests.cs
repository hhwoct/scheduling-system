using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task UpdateAsync_MaxWeeklyHours_SyncsFollowDefaultEmployeesOnly()
    {
        var rule = await SeedRuleAsync(_factory, value: "48");
        var db = _factory.CreateDbContext();
        db.Employees.AddRange(
            new EmployeeEntity { StoreId = 1, EmployeeNo = "E001", Name = "跟随默认", Department = "楼面", MaxWeeklyHours = 48, WeeklyHoursFollowDefault = 1, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new EmployeeEntity { StoreId = 1, EmployeeNo = "E002", Name = "个人覆盖", Department = "楼面", MaxWeeklyHours = 50, WeeklyHoursFollowDefault = 0, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService();
        await service.UpdateAsync(rule.Id, new RuleConfigUpdateRequest("60", 1, Version: rule.Version), 1, 9, "管理员", CancellationToken.None);

        var reloaded = _factory.CreateDbContext();
        var followDefault = await reloaded.Employees.SingleAsync(x => x.EmployeeNo == "E001");
        var custom = await reloaded.Employees.SingleAsync(x => x.EmployeeNo == "E002");
        Assert.Equal(60m, followDefault.MaxWeeklyHours);
        Assert.Equal(50m, custom.MaxWeeklyHours); // 个人覆盖不被全局修改重置
    }

    [Fact]
    public async Task UpdateAsync_JsonTypeRule_AcceptsJsonValue()
    {
        // 单日最大工时按岗位配置：value_type=json，保存 JSON 字符串不应被数字校验拦截
        await SeedRuleAsync(_factory, key: "max_daily_work_hours", value: "12", valueType: "json");
        var service = CreateService();
        var json = "{\"default\":12,\"保洁\":10,\"楼面\":11}";
        var result = await service.UpdateAsync(1, new RuleConfigUpdateRequest(json, 1), 1, 9, "a", CancellationToken.None);

        Assert.Equal(json, result.RuleValue);
    }
}
