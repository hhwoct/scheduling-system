using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.Schedules;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>
/// 排班计划服务单元测试：通过内存 SQLite 种子数据驱动完整算法链路
/// （生成 → 列表 → 汇总 → 发布）。
/// </summary>
public sealed class ScheduleServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private static readonly DateOnly Start = new(2026, 8, 3); // 周一
    private static readonly DateOnly End = new(2026, 8, 9);

    private ScheduleService CreateService()
        => new(_factory.CreateDbContext(), new SchedulingEngine(_factory.CreateDbContext()), _audit);

    /// <summary>种子数据：1 店、2 员工、1 工作站、1 班次、覆盖 7 天的需求。</summary>
    private async Task SeedStoreDataAsync()
    {
        var db = _factory.CreateDbContext();

        db.Workstations.Add(new WorkstationEntity
        {
            StoreId = 1, Code = "SVC", Name = "楼面", SortOrder = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var ws = await db.Workstations.AsNoTracking().FirstAsync(x => x.Code == "SVC");

        db.ShiftTemplates.Add(new ShiftTemplateEntity
        {
            StoreId = 1, Code = "S4", Name = "楼面A班", StartTime = new TimeSpan(19, 0, 0), EndTime = new TimeSpan(22, 0, 0),
            IsCrossDay = 0, Priority = 7, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var shift = await db.ShiftTemplates.AsNoTracking().FirstAsync(x => x.Code == "S4");
        db.ShiftWorkstations.Add(new ShiftWorkstationEntity { ShiftTemplateId = shift.Id, WorkstationId = ws.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Employees.AddRange(
            new EmployeeEntity
            {
                StoreId = 1, EmployeeNo = "E001", Name = "张楼面", Department = "楼面", PrimaryPosition = "服务员",
                MaxWeeklyHours = 48, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            },
            new EmployeeEntity
            {
                StoreId = 1, EmployeeNo = "E002", Name = "李厨房", Department = "厨房", PrimaryPosition = "厨师",
                MaxWeeklyHours = 48, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var e1 = await db.Employees.AsNoTracking().FirstAsync(x => x.EmployeeNo == "E001");

        db.EmployeeSkills.Add(new EmployeeSkillEntity
        {
            EmployeeId = e1.Id, WorkstationId = ws.Id, SkillScore = 5, IsPrimarySkill = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        for (var i = 0; i < 7; i++)
        {
            var date = Start.AddDays(i);
            db.DateParameters.Add(new DateParameterEntity
            {
                // WeekDay 与生产一致（MySQL DAYOFWEEK：1=周日 … 7=周六）
                StoreId = 1, WorkDate = date, WeekDay = (int)date.DayOfWeek + 1,
                DayType = "WORKDAY", CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        // 审查同步（60d4c6c 起默认 6.5h）：测试班次仅 3 小时（19:00-22:00），
        // 显式配置 min_daily_work_hours=0（不限制），否则正式员工因每日最低工时被整体拒排。
        db.RuleConfigs.Add(new RuleConfigEntity
        {
            StoreId = 1, RuleKey = "min_daily_work_hours", RuleName = "正式员工每日最低工时",
            RuleValue = "0", ValueType = "number", Remark = "测试：不限制每日最低工时",
            Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        foreach (var slot in SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay))
        {
            db.StaffingRequirements.Add(new StaffingRequirementEntity
            {
                StoreId = 1, DayType = "WORKDAY", WorkstationId = ws.Id, TimeSlot = slot,
                RequiredCount = 1, IdealCount = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GenerateAsync_ValidRange_CreatesPlanWithResults()
    {
        await SeedStoreDataAsync();
        var service = CreateService();

        var result = await service.GenerateAsync(
            new GenerateScheduleRequest(Start, End),
            1, 9, "管理员", CancellationToken.None);

        Assert.True(result.PlanId > 0);
        Assert.True(result.ShiftAssignmentCount > 0);
        Assert.True(result.WorkstationAssignmentCount > 0);
        Assert.True(result.SummaryCount > 0);
        Assert.Contains("GENERATE_SCHEDULE", _audit.Entries.Select(e => e.ActionType));

        var db = _factory.CreateDbContext();
        var plan = await db.SchedulePlans.AsNoTracking().FirstAsync(x => x.Id == result.PlanId);
        Assert.Equal("DRAFT", plan.Status);
        Assert.Equal(Start, plan.StartDate);
        Assert.Equal(End, plan.EndDate);
        Assert.NotEmpty(await db.ScheduleResults.AsNoTracking().Where(x => x.PlanId == plan.Id).ToListAsync());
        Assert.NotEmpty(await db.ScheduleSummaries.AsNoTracking().Where(x => x.PlanId == plan.Id).ToListAsync());
    }

    [Fact]
    public async Task GenerateAsync_EndBeforeStart_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateAsync(new GenerateScheduleRequest(End, Start), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_DATE_RANGE", ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_PeriodOver31Days_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateAsync(new GenerateScheduleRequest(Start, Start.AddDays(40)), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_DATE_RANGE", ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_PublishedPlanForSamePeriod_ThrowsDuplicate()
    {
        await SeedStoreDataAsync();
        var db = _factory.CreateDbContext();
        db.SchedulePlans.Add(new SchedulePlanEntity
        {
            StoreId = 1, PlanName = "已有排班", StartDate = Start, EndDate = End, Status = "PUBLISHED",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "a", CancellationToken.None));
        Assert.Equal("DUPLICATE_SCHEDULE_PLAN", ex.ErrorCode);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsPlansWithCounts()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        var generated = await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);

        var page = await service.ListPlansAsync(1, 20, 1, null, CancellationToken.None);

        Assert.Equal(1, page.Total);
        var item = Assert.Single(page.Items);
        Assert.Equal(generated.PlanId, item.Id);
        Assert.Equal("DRAFT", item.Status);
    }

    [Fact]
    public async Task ListPlansAsync_FiltersByStatus()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);

        var draft = await service.ListPlansAsync(1, 20, 1, "DRAFT", CancellationToken.None);
        var published = await service.ListPlansAsync(1, 20, 1, "PUBLISHED", CancellationToken.None);

        Assert.Equal(1, draft.Total);
        Assert.Equal(0, published.Total);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsRestWorkAndIssues()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        var generated = await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);

        var summary = await service.GetSummaryAsync(generated.PlanId, 1, CancellationToken.None);

        Assert.Equal(generated.PlanId, summary.PlanId);
        Assert.True(summary.WorkDayCount > 0);
        Assert.True(summary.TotalWorkHours > 0);
    }

    [Fact]
    public async Task GetSummaryAsync_UnknownPlan_ThrowsNotFound()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetSummaryAsync(999, 1, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_PublishesPlanAndCreatesNotifications()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        var generated = await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);

        await service.PublishAsync(generated.PlanId, 1, 9, "管理员", force: false, CancellationToken.None);

        var db = _factory.CreateDbContext();
        var plan = await db.SchedulePlans.AsNoTracking().FirstAsync(x => x.Id == generated.PlanId);
        Assert.Equal("PUBLISHED", plan.Status);
        Assert.NotNull(plan.PublishedAt);
        Assert.True(await db.ScheduleResults.AsNoTracking().AllAsync(x => x.PlanId == plan.Id && x.Status == "PUBLISHED"));
        Assert.True(await db.Notifications.AsNoTracking().AnyAsync(x => x.NotificationType == "SCHEDULE_PUBLISHED"));
        Assert.Contains("PUBLISH_SCHEDULE", _audit.Entries.Select(e => e.ActionType));
    }

    [Fact]
    public async Task PublishAsync_AlreadyPublished_Throws()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        var generated = await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);
        await service.PublishAsync(generated.PlanId, 1, 9, "管理员", force: false, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.PublishAsync(generated.PlanId, 1, 9, "管理员", force: false, CancellationToken.None));
        Assert.Equal("ALREADY_PUBLISHED", ex.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_ErrorIssues_BlockedWithoutForce()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        var generated = await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);

        var db = _factory.CreateDbContext();
        db.ScheduleIssues.Add(new ScheduleIssueEntity
        {
            PlanId = generated.PlanId, StoreId = 1, IssueType = "SKILL_MISMATCH", Severity = "ERROR",
            Description = "严重违规", Status = "OPEN", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.PublishAsync(generated.PlanId, 1, 9, "管理员", force: false, CancellationToken.None));
        Assert.Equal("HAS_ERROR_ISSUES", ex.ErrorCode);

        // force=true 可绕过
        await service.PublishAsync(generated.PlanId, 1, 9, "管理员", force: true, CancellationToken.None);
        var plan = await db.SchedulePlans.AsNoTracking().FirstAsync(x => x.Id == generated.PlanId);
        Assert.Equal("PUBLISHED", plan.Status);
    }

    [Fact]
    public async Task PublishAsync_UnknownPlan_ThrowsNotFound()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.PublishAsync(999, 1, 9, "a", force: false, CancellationToken.None));
    }

    [Fact]
    public async Task UnpublishAsync_PublishedPlan_RevertsToDraftAndNotifies()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        var generated = await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);
        await service.PublishAsync(generated.PlanId, 1, 9, "管理员", force: false, CancellationToken.None);

        await service.UnpublishAsync(generated.PlanId, 1, 9, "管理员", CancellationToken.None);

        var db = _factory.CreateDbContext();
        var plan = await db.SchedulePlans.AsNoTracking().FirstAsync(x => x.Id == generated.PlanId);
        Assert.Equal("DRAFT", plan.Status);
        Assert.Null(plan.PublishedAt);
        Assert.True(await db.ScheduleResults.AsNoTracking().AllAsync(x => x.PlanId == plan.Id && x.Status == "DRAFT"));
        Assert.True(await db.Notifications.AsNoTracking().AnyAsync(x => x.NotificationType == "SCHEDULE_UNPUBLISHED"));
        Assert.Contains("UNPUBLISH_SCHEDULE", _audit.Entries.Select(e => e.ActionType));
    }

    [Fact]
    public async Task UnpublishAsync_DraftPlan_Throws()
    {
        await SeedStoreDataAsync();
        var service = CreateService();
        var generated = await service.GenerateAsync(new GenerateScheduleRequest(Start, End), 1, 9, "管理员", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UnpublishAsync(generated.PlanId, 1, 9, "管理员", CancellationToken.None));
        Assert.Equal("NOT_PUBLISHED", ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_NoDateParameters_ThrowsBusinessException()
    {
        await SeedStoreDataAsync();
        var service = CreateService();

        // 种子数据只覆盖 8-03 ~ 8-09，8-10 之后没有任何日期参数 → 应显式报错而非静默产出空计划
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateAsync(
                new GenerateScheduleRequest(Start.AddDays(7), Start.AddDays(13)),
                1, 9, "管理员", CancellationToken.None));

        Assert.Equal("INCOMPLETE_DATE_PARAMETERS", ex.ErrorCode);

        var db = _factory.CreateDbContext();
        Assert.False(await db.SchedulePlans.AnyAsync());
    }
}
