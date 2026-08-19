using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.EmployeeSkills;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>员工技能服务单元测试。</summary>
public sealed class EmployeeSkillServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private EmployeeSkillService CreateService()
        => new(_factory.CreateDbContext(), _audit);

    private static async Task<long> SeedEmployeeAsync(TestDbContextFactory factory, string no = "E001", int isParttime = 0)
    {
        var db = factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1, EmployeeNo = no, Name = "员工" + no, Department = "楼面", Status = 1,
            IsParttime = isParttime, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private static async Task<long> SeedWorkstationAsync(TestDbContextFactory factory, string code = "WS", int isLowSkill = 0)
    {
        var db = factory.CreateDbContext();
        var ws = new WorkstationEntity
        {
            StoreId = 1, Code = code, Name = "工作站" + code, SortOrder = 1, IsLowSkill = isLowSkill, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Workstations.Add(ws);
        await db.SaveChangesAsync();
        return ws.Id;
    }

    [Fact]
    public async Task SaveAsync_PersistsSkills()
    {
        var employeeId = await SeedEmployeeAsync(_factory);
        var ws1 = await SeedWorkstationAsync(_factory, "WS1");
        var ws2 = await SeedWorkstationAsync(_factory, "WS2");

        var service = CreateService();
        await service.SaveAsync(
            employeeId,
            new EmployeeSkillSaveRequest(new List<EmployeeSkillSaveItem>
            {
                new(ws1, 5, 1),
                new(ws2, 3, 0)
            }),
            1, 9, "管理员", CancellationToken.None);

        var matrix = await service.GetByEmployeeAsync(employeeId, 1, CancellationToken.None);
        Assert.Equal(2, matrix.Skills.Count);
        Assert.Contains(matrix.Skills, s => s.WorkstationId == ws1 && s.SkillScore == 5 && s.IsPrimarySkill == 1);
        Assert.Contains(_audit.Entries, e => e.ActionType == "SAVE_EMPLOYEE_SKILLS");
    }

    [Fact]
    public async Task SaveAsync_EmptySkills_Throws()
    {
        var employeeId = await SeedEmployeeAsync(_factory);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(employeeId, new EmployeeSkillSaveRequest(new List<EmployeeSkillSaveItem>()), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_SKILLS", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_TwoPrimarySkills_Throws()
    {
        var employeeId = await SeedEmployeeAsync(_factory);
        var ws1 = await SeedWorkstationAsync(_factory, "WS1");
        var ws2 = await SeedWorkstationAsync(_factory, "WS2");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(
                employeeId,
                new EmployeeSkillSaveRequest(new List<EmployeeSkillSaveItem>
                {
                    new(ws1, 5, 1),
                    new(ws2, 4, 1)
                }),
                1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_SKILL", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateCellAsync_SkillScoreOutOfRange_Throws()
    {
        var employeeId = await SeedEmployeeAsync(_factory);
        var ws1 = await SeedWorkstationAsync(_factory, "WS1");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateCellAsync(new SkillMatrixCellUpdateRequest(employeeId, ws1, 6, 1), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_SKILL_SCORE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateCellAsync_OnlyOnePrimarySkillKept()
    {
        var employeeId = await SeedEmployeeAsync(_factory);
        var ws1 = await SeedWorkstationAsync(_factory, "WS1");
        var ws2 = await SeedWorkstationAsync(_factory, "WS2");

        var service = CreateService();
        await service.UpdateCellAsync(new SkillMatrixCellUpdateRequest(employeeId, ws1, 5, 1), 1, 9, "a", CancellationToken.None);
        await service.UpdateCellAsync(new SkillMatrixCellUpdateRequest(employeeId, ws2, 4, 1), 1, 9, "a", CancellationToken.None);

        var matrix = await service.GetByEmployeeAsync(employeeId, 1, CancellationToken.None);
        Assert.Single(matrix.Skills.Where(s => s.IsPrimarySkill == 1));
        Assert.Equal(ws2, matrix.Skills.Single(s => s.IsPrimarySkill == 1).WorkstationId);
    }

    [Fact]
    public async Task SetGeneralistAsync_EnablesLowSkillWorkstationsAtMinScore3()
    {
        var employeeId = await SeedEmployeeAsync(_factory);
        var ws1 = await SeedWorkstationAsync(_factory, "LOW1", isLowSkill: 1);
        var ws2 = await SeedWorkstationAsync(_factory, "LOW2", isLowSkill: 1);
        var ws3 = await SeedWorkstationAsync(_factory, "HIGH", isLowSkill: 0);

        var service = CreateService();
        var result = await service.SetGeneralistAsync(
            new SkillMatrixGeneralistRequest(employeeId, 1), 1, 9, "管理员", CancellationToken.None);

        Assert.Equal(1, result.IsGeneralist);
        // 只影响低技能工作站，且分数 >= 3
        Assert.Equal(2, result.Cells.Count);
        Assert.All(result.Cells, c => Assert.True(c.SkillScore >= 3));
        Assert.DoesNotContain(result.Cells, c => c.WorkstationId == ws3);
        Assert.Contains(_audit.Entries, e => e.ActionType == "SET_EMPLOYEE_GENERALIST");
    }

    [Fact]
    public async Task SetGeneralistAsync_ParttimeEmployee_Throws()
    {
        var employeeId = await SeedEmployeeAsync(_factory, isParttime: 1);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SetGeneralistAsync(new SkillMatrixGeneralistRequest(employeeId, 1), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_GENERALIST_EMPLOYEE", ex.ErrorCode);
    }

    [Fact]
    public async Task GetByEmployeeAsync_NonexistentEmployee_ThrowsNotFound()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetByEmployeeAsync(999, 1, CancellationToken.None));
    }

    [Fact]
    public async Task GetStoreMatrixAsync_ExcludesParttimeEmployees()
    {
        var full = await SeedEmployeeAsync(_factory, "E001", isParttime: 0);
        var part = await SeedEmployeeAsync(_factory, "E002", isParttime: 1);
        var ws1 = await SeedWorkstationAsync(_factory, "WS1");

        var service = CreateService();
        await service.UpdateCellAsync(new SkillMatrixCellUpdateRequest(full, ws1, 5, 1), 1, 9, "a", CancellationToken.None);
        await service.UpdateCellAsync(new SkillMatrixCellUpdateRequest(part, ws1, 4, 0), 1, 9, "a", CancellationToken.None);

        var overview = await service.GetStoreMatrixAsync(1, CancellationToken.None);
        Assert.Single(overview.Employees);
        Assert.Equal("E001", overview.Employees[0].EmployeeNo);
    }
}
