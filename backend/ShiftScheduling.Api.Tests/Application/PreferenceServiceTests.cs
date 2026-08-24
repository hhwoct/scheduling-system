using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShiftScheduling.Api.Application.Preferences;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>
/// 偏好学习服务单元测试：重建聚合、贴合率评估、统计口径。
/// </summary>
public sealed class PreferenceServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private PreferenceService CreateService()
        => new(_factory.CreateDbContext(), new ConfigurationBuilder().Build());

    private async Task SeedBaselineAsync()
    {
        var db = _factory.CreateDbContext();

        db.Stores.Add(new StoreEntity { Id = 1, Code = "KM", Name = "门店", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Workstations.Add(new WorkstationEntity { Id = 1, StoreId = 1, Code = "SVC", Name = "服务岗", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Employees.Add(new EmployeeEntity { Id = 1, StoreId = 1, EmployeeNo = "E001", Name = "甲", Department = "楼面", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.ShiftTemplates.Add(new ShiftTemplateEntity { Id = 1, StoreId = 1, Code = "S4", Name = "楼面A班", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // 两期已发布计划：P1（学习期）、P2（评估期）
        db.SchedulePlans.Add(new SchedulePlanEntity
        {
            Id = 1, StoreId = 1, PlanName = "第一期", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 7),
            Status = "PUBLISHED", PublishedAt = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.SchedulePlans.Add(new SchedulePlanEntity
        {
            Id = 2, StoreId = 1, PlanName = "第二期", StartDate = new DateOnly(2026, 9, 8), EndDate = new DateOnly(2026, 9, 14),
            Status = "PUBLISHED", PublishedAt = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // P1：E001 上 S4 服务岗 5 天
        for (var i = 0; i < 5; i++)
        {
            db.ScheduleSummaries.Add(new ScheduleSummaryEntity
            {
                StoreId = 1, PlanId = 1, EmployeeId = 1, WorkDate = new DateOnly(2026, 9, 1).AddDays(i),
                IsRestDay = 0, ShiftTemplateId = 1,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }

        // P2（评估期）：E001 上 S4 服务岗 4 天 + 休息 1 天
        for (var i = 0; i < 4; i++)
        {
            db.ScheduleSummaries.Add(new ScheduleSummaryEntity
            {
                StoreId = 1, PlanId = 2, EmployeeId = 1, WorkDate = new DateOnly(2026, 9, 8).AddDays(i),
                IsRestDay = 0, ShiftTemplateId = 1,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        db.ScheduleSummaries.Add(new ScheduleSummaryEntity
        {
            StoreId = 1, PlanId = 2, EmployeeId = 1, WorkDate = new DateOnly(2026, 9, 12),
            IsRestDay = 1, ShiftTemplateId = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RebuildAsync_AggregatesLearningPeriods_AndScoresAdherence()
    {
        await SeedBaselineAsync();
        var service = CreateService();

        var stats = await service.RebuildAsync(1, CancellationToken.None);

        // 学习期 = P1（1 期），样本 = 5 条
        Assert.Equal(1, stats.LearnedPeriods);
        Assert.Equal(5, stats.SampleDays);

        // 偏好表：E001 × WORKDAY × 班次 S4（freq=5，工作站列为 NULL）
        var db = _factory.CreateDbContext();
        var pref = await db.EmployeePreferences.AsNoTracking().FirstAsync();
        Assert.Equal(1, pref.EmployeeId);
        Assert.Equal("S4", pref.ShiftCode);
        Assert.Null(pref.WorkstationId);
        Assert.Equal(5, pref.Freq);

        // 贴合率：P2 的 4 天上班组合在偏好中（freq 5 的组合）→ 4/5 = 80%
        Assert.Equal(80.0m, stats.AdherencePct);

        // 趋势记录已写入
        var trend = await db.PreferenceTrends.AsNoTracking().FirstAsync();
        Assert.Equal(2, trend.PlanId);
        Assert.Equal(80.0m, trend.AdherencePct);
        Assert.Equal(5, trend.SampleDays);
    }

    [Fact]
    public async Task RebuildAsync_NoPublishedPlans_ReturnsEmpty()
    {
        var db = _factory.CreateDbContext();
        db.Stores.Add(new StoreEntity { Id = 1, Code = "KM", Name = "门店", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService();
        var stats = await service.RebuildAsync(1, CancellationToken.None);

        Assert.Equal(0, stats.LearnedPeriods);
        Assert.Equal(0, stats.SampleDays);
        Assert.Equal(0, stats.PreferenceRows);
    }

    [Fact]
    public async Task GetStatsAsync_ReflectsRebuiltState()
    {
        await SeedBaselineAsync();
        var service = CreateService();
        await service.RebuildAsync(1, CancellationToken.None);

        var stats = await service.GetStatsAsync(1, CancellationToken.None);
        Assert.Equal(1, stats.LearnedPeriods);
        Assert.Equal(5, stats.SampleDays);
        Assert.Equal(80.0m, stats.AdherencePct);
        Assert.Equal(1, stats.PreferenceRows);
    }

    [Fact]
    public async Task RebuildAsync_SkipsPartTimerRestSamples_ButKeepsTheirShiftSamples()
    {
        // 兼职（is_parttime=1）无「休息」语义：空闲日 ≠ 店长安排休息，
        // 其休息样本是伪信号（会把「没排班」学成「店长习惯让他休」）——必须排除；
        // 班次样本保留（店长对兼职的使用习惯仍有学习价值）。
        var db = _factory.CreateDbContext();

        db.Stores.Add(new StoreEntity { Id = 1, Code = "KM", Name = "门店", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Workstations.Add(new WorkstationEntity { Id = 1, StoreId = 1, Code = "SVC", Name = "服务岗", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Employees.Add(new EmployeeEntity { Id = 1, StoreId = 1, EmployeeNo = "E001", Name = "甲", Department = "楼面", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Employees.Add(new EmployeeEntity { Id = 2, StoreId = 1, EmployeeNo = "E101", Name = "兼甲", Department = "楼面", IsParttime = 1, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.ShiftTemplates.Add(new ShiftTemplateEntity { Id = 1, StoreId = 1, Code = "S4", Name = "楼面A班", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.SchedulePlans.Add(new SchedulePlanEntity
        {
            Id = 1, StoreId = 1, PlanName = "第一期", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 7),
            Status = "PUBLISHED", PublishedAt = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.SchedulePlans.Add(new SchedulePlanEntity
        {
            Id = 2, StoreId = 1, PlanName = "第二期", StartDate = new DateOnly(2026, 9, 8), EndDate = new DateOnly(2026, 9, 14),
            Status = "PUBLISHED", PublishedAt = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // 学习期（P1）：兼职 E101 上班 3 天（S4，freq=3 达覆盖率阈值）+ 空闲 4 天（is_rest_day=1，伪休息样本）
        for (var i = 0; i < 3; i++)
        {
            db.ScheduleSummaries.Add(new ScheduleSummaryEntity
            {
                StoreId = 1, PlanId = 1, EmployeeId = 2, WorkDate = new DateOnly(2026, 9, 1).AddDays(i),
                IsRestDay = 0, ShiftTemplateId = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        for (var i = 3; i < 7; i++)
        {
            db.ScheduleSummaries.Add(new ScheduleSummaryEntity
            {
                StoreId = 1, PlanId = 1, EmployeeId = 2, WorkDate = new DateOnly(2026, 9, 1).AddDays(i),
                IsRestDay = 1, ShiftTemplateId = null, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var service = CreateService();
        await service.RebuildAsync(1, CancellationToken.None);

        // 兼职的班次样本存在（3 次 S4，达覆盖率阈值）
        var shiftSample = await db.EmployeePreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == 2 && x.ShiftCode == "S4");
        Assert.NotNull(shiftSample);
        Assert.Equal(3, shiftSample.Freq);

        // 兼职的休息样本被排除（两列皆 NULL 的记录不存在）
        var restSample = await db.EmployeePreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == 2 && x.ShiftCode == null && x.WorkstationId == null);
        Assert.Null(restSample);

        // 覆盖率按全职计算：1 名全职中 0 人有样本（全职 E001 无记录）→ 0%
        var stats = await service.GetStatsAsync(1, CancellationToken.None);
        Assert.Equal(0m, stats.CoveragePct);
    }
}
