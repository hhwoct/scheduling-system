using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.ShiftTemplates;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>班次模板服务单元测试。</summary>
public sealed class ShiftTemplateServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private ShiftTemplateService CreateService()
        => new(_factory.CreateDbContext(), _audit);

    [Fact]
    public async Task ListAllAsync_IncludesCoveredWorkstationNames()
    {
        var db = _factory.CreateDbContext();
        var ws = new WorkstationEntity
        {
            StoreId = 1, Code = "WS01", Name = "前台", SortOrder = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Workstations.Add(ws);
        await db.SaveChangesAsync();

        var shift = new ShiftTemplateEntity
        {
            StoreId = 1, Code = "S1", Name = "前台班", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(18, 0, 0),
            IsCrossDay = 0, Priority = 1, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.ShiftTemplates.Add(shift);
        await db.SaveChangesAsync();
        db.ShiftWorkstations.Add(new ShiftWorkstationEntity { ShiftTemplateId = shift.Id, WorkstationId = ws.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService();
        var list = await service.ListAllAsync(1, CancellationToken.None);

        var item = Assert.Single(list);
        Assert.Equal("S1", item.Code);
        Assert.Equal(new[] { "前台" }, item.CoveredWorkstations);
    }

    [Fact]
    public async Task UpdateAsync_EmptyName_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(1, new ShiftTemplateUpdateRequest("", new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), 0, 1, 1), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_SHIFT_TEMPLATE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_SameStartAndEnd_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(1, new ShiftTemplateUpdateRequest("班次", new TimeSpan(9, 0, 0), new TimeSpan(9, 0, 0), 0, 1, 1), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_SHIFT_TEMPLATE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesAndAudits()
    {
        var db = _factory.CreateDbContext();
        var shift = new ShiftTemplateEntity
        {
            StoreId = 1, Code = "S1", Name = "旧名", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(18, 0, 0),
            IsCrossDay = 0, Priority = 1, Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.ShiftTemplates.Add(shift);
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.UpdateAsync(
            shift.Id,
            new ShiftTemplateUpdateRequest("新名", new TimeSpan(10, 0, 0), new TimeSpan(19, 0, 0), 0, 2, 1),
            1, 9, "管理员", CancellationToken.None);

        Assert.Equal("新名", result.Name);
        Assert.Equal(new TimeSpan(10, 0, 0), result.StartTime);
        Assert.Contains(_audit.Entries, e => e.ActionType == "UPDATE_SHIFT_TEMPLATE");
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(999, new ShiftTemplateUpdateRequest("新名", new TimeSpan(10, 0, 0), new TimeSpan(19, 0, 0), 0, 2, 1), 1, 9, "a", CancellationToken.None));
    }
}
