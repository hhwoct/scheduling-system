using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.Workstations;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>工作站服务单元测试。</summary>
public sealed class WorkstationServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private WorkstationService CreateService()
        => new(_factory.CreateDbContext(), _audit);

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesWorkstation()
    {
        var service = CreateService();
        var result = await service.CreateAsync(
            new WorkstationCreateRequest("WS01", "前台", 1, "备注", 0),
            1, 9, "管理员", CancellationToken.None);

        Assert.True(result.Id > 0);
        Assert.Equal("WS01", result.Code);
        Assert.Contains(_audit.Entries, e => e.ActionType == "CREATE_WORKSTATION");
    }

    [Fact]
    public async Task CreateAsync_MissingCode_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new WorkstationCreateRequest("", "前台", 1, null, 0), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_WORKSTATION", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_Throws()
    {
        var db = _factory.CreateDbContext();
        db.Workstations.Add(new WorkstationEntity
        {
            StoreId = 1, Code = "WS01", Name = "旧站", SortOrder = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new WorkstationCreateRequest("WS01", "新站", 2, null, 0), 1, 9, "a", CancellationToken.None));
        Assert.Equal("DUPLICATE_WORKSTATION", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesNameAndStatus()
    {
        var db = _factory.CreateDbContext();
        var ws = new WorkstationEntity
        {
            StoreId = 1, Code = "WS01", Name = "旧名", SortOrder = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Workstations.Add(ws);
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.UpdateAsync(ws.Id, new WorkstationUpdateRequest("新名", "新备注", 1, 1), 1, 9, "管理员", CancellationToken.None);

        Assert.Equal("新名", result.Name);
        Assert.Equal(1, result.IsLowSkill);
        Assert.Contains(_audit.Entries, e => e.ActionType == "UPDATE_WORKSTATION");
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(999, new WorkstationUpdateRequest("新名", null, 1), 1, 9, "a", CancellationToken.None));
    }

    [Fact]
    public async Task ListAllAsync_ExcludesInactiveByDefault()
    {
        var db = _factory.CreateDbContext();
        db.Workstations.AddRange(
            new WorkstationEntity { StoreId = 1, Code = "A", Name = "启用", SortOrder = 1, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new WorkstationEntity { StoreId = 1, Code = "B", Name = "停用", SortOrder = 2, Status = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new WorkstationEntity { StoreId = 2, Code = "C", Name = "别店", SortOrder = 1, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService();
        var list = await service.ListAllAsync(1, includeInactive: false, CancellationToken.None);
        Assert.Single(list);

        var all = await service.ListAllAsync(1, includeInactive: true, CancellationToken.None);
        Assert.Equal(2, all.Count);
    }
}
