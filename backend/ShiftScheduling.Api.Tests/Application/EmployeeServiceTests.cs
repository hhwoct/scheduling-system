using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.Employees;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>员工档案服务单元测试。</summary>
public sealed class EmployeeServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private EmployeeService CreateService()
        => new(_factory.CreateDbContext(), _audit);

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesEmployee()
    {
        var service = CreateService();
        var result = await service.CreateAsync(
            new EmployeeUpsertRequest("E001", "张三", "13800000000", "楼面", null, "服务员", 48),
            1, 9, "管理员", CancellationToken.None);

        Assert.True(result.Id > 0);
        Assert.Equal("E001", result.EmployeeNo);
        Assert.Equal("138****0000", result.Phone); // 脱敏
        Assert.Contains(_audit.Entries, e => e.ActionType == "CREATE_EMPLOYEE");
    }

    [Fact]
    public async Task CreateAsync_EmptyFields_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new EmployeeUpsertRequest("", "", null, "楼面", null, null, 48), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_EMPLOYEE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_InvalidWeeklyHours_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new EmployeeUpsertRequest("E001", "张三", null, "楼面", null, null, 200, 0), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_EMPLOYEE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_InvalidPhone_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new EmployeeUpsertRequest("E101", "张三", "12345", "楼面", null, null, 48), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_EMPLOYEE", ex.ErrorCode);
        Assert.Contains("手机号", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ValidPhone_Passes()
    {
        var service = CreateService();
        var result = await service.CreateAsync(
            new EmployeeUpsertRequest("E102", "张三", "13800001234", "楼面", null, null, 48), 1, 9, "a", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("E102", result.EmployeeNo);
    }

    [Fact]
    public async Task CreateAsync_FollowDefault_UsesGlobalRuleValue()
    {
        // 种子：全局最大周工时 = 60；跟随默认时个人周工时上限取规则值而非表单值
        var db = _factory.CreateDbContext();
        db.RuleConfigs.Add(new RuleConfigEntity
        {
            StoreId = 1, RuleKey = "max_weekly_hours", RuleName = "最大周工时", RuleValue = "60", ValueType = "number",
            Status = 1, Version = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.CreateAsync(
            new EmployeeUpsertRequest("E103", "张三", null, "楼面", null, null, 999, 1),
            1, 9, "管理员", CancellationToken.None);

        Assert.Equal(60m, result.MaxWeeklyHours);
        Assert.Equal(1, result.WeeklyHoursFollowDefault);
    }

    [Fact]
    public async Task UpdateAsync_InvalidPhone_Throws()
    {
        var db = _factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "旧名", Department = "楼面", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(
                emp.Id,
                new EmployeeUpsertRequest("E001", "新名", "abc", "厨房", null, null, 40),
                1, 9, "管理员", CancellationToken.None));
        Assert.Equal("INVALID_EMPLOYEE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmployeeNo_Throws()
    {
        var db = _factory.CreateDbContext();
        db.Employees.Add(new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "旧员工", Department = "楼面", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new EmployeeUpsertRequest("E001", "张三", null, "楼面", null, null, 48), 1, 9, "a", CancellationToken.None));
        Assert.Equal("EMPLOYEE_NO_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldsAndAudits()
    {
        var db = _factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "旧名", Department = "楼面", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.UpdateAsync(
            emp.Id,
            new EmployeeUpsertRequest("E001", "新名", null, "厨房", null, null, 40),
            1, 9, "管理员", CancellationToken.None);

        Assert.Equal("新名", result.Name);
        Assert.Equal("厨房", result.Department);
        Assert.Contains(_audit.Entries, e => e.ActionType == "UPDATE_EMPLOYEE");
    }

    [Fact]
    public async Task UpdateAsync_MaskedPhone_KeepsOriginalPhone()
    {
        // 审查修复（H1）：列表/详情返回脱敏手机号（138****5678），前端编辑回填后原样提交，
        // 后端应识别脱敏值并保留原手机号，而不是校验失败或覆盖真实号码。
        var db = _factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "张三", Department = "楼面",
            Phone = "13812345678", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.UpdateAsync(
            emp.Id,
            new EmployeeUpsertRequest("E001", "新名", "138****5678", "厨房", null, null, 40),
            1, 9, "管理员", CancellationToken.None);

        Assert.Equal("新名", result.Name);
        Assert.Equal("厨房", result.Department);

        var reloaded = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == emp.Id);
        Assert.Equal("13812345678", reloaded.Phone); // 原手机号未被脱敏值覆盖
    }

    [Fact]
    public async Task UpdateAsync_RealPhone_IsUpdated()
    {
        var db = _factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "张三", Department = "楼面",
            Phone = "13812345678", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.UpdateAsync(
            emp.Id,
            new EmployeeUpsertRequest("E001", "张三", "13900001111", "楼面", null, null, 48),
            1, 9, "管理员", CancellationToken.None);

        var reloaded = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == emp.Id);
        Assert.Equal("13900001111", reloaded.Phone); // 真实新号码正常更新
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(999, new EmployeeUpsertRequest("E001", "张三", null, "楼面", null, null, 48), 1, 9, "a", CancellationToken.None));
    }

    [Fact]
    public async Task DeactivateAsync_SetsStatusZero()
    {
        var db = _factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "张三", Department = "楼面", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var service = CreateService();
        await service.DeactivateAsync(emp.Id, 1, 9, "管理员", CancellationToken.None);

        // 用全新上下文读取，避免返回旧上下文缓存的被跟踪实体
        var freshDb = _factory.CreateDbContext();
        var reloaded = await freshDb.Employees.AsNoTracking().SingleAsync(x => x.Id == emp.Id);
        Assert.Equal(0, reloaded.Status);
        Assert.Contains(_audit.Entries, e => e.ActionType == "DEACTIVATE_EMPLOYEE");
    }

    [Fact]
    public async Task QueryAsync_FiltersByNameAndDepartment_Paginates()
    {
        var db = _factory.CreateDbContext();
        db.Employees.AddRange(
            new EmployeeEntity { StoreId = 1, EmployeeNo = "E001", Name = "张三", Department = "楼面", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new EmployeeEntity { StoreId = 1, EmployeeNo = "E002", Name = "李四", Department = "厨房", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new EmployeeEntity { StoreId = 2, EmployeeNo = "E003", Name = "张三", Department = "楼面", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.QueryAsync(new EmployeeQueryRequest(Name: "张"), 1, false, CancellationToken.None);

        // 只查门店 1 的张三
        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task QueryAsync_DefaultStatusIsActive()
    {
        var db = _factory.CreateDbContext();
        db.Employees.AddRange(
            new EmployeeEntity { StoreId = 1, EmployeeNo = "E001", Name = "在职", Department = "楼面", Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new EmployeeEntity { StoreId = 1, EmployeeNo = "E002", Name = "停用", Department = "楼面", Status = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.QueryAsync(new EmployeeQueryRequest(), 1, false, CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("E001", result.Items[0].EmployeeNo);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsMaskedPhone()
    {
        var db = _factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "张三", Department = "楼面", Phone = "13812345678", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var service = CreateService();
        var detail = await service.GetByIdAsync(emp.Id, 1, CancellationToken.None);
        Assert.Equal("138****5678", detail.Phone);
    }

    [Fact]
    public async Task GetByIdAsync_OtherStore_ThrowsNotFound()
    {
        var db = _factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = "E001", Name = "张三", Department = "楼面", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(emp.Id, 2, CancellationToken.None));
    }
}