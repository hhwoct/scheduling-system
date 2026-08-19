using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.PeakHours;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>高峰禁休时段服务单元测试。</summary>
public sealed class PeakHourServiceTests
{
    private readonly InMemoryTestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private PeakHourService CreateService()
        => new(_factory.CreateDbContext(), _audit);

    [Fact]
    public async Task CreateAsync_ValidRange_CreatesPeakHour()
    {
        var service = CreateService();
        var result = await service.CreateAsync(
            new PeakHourUpsertRequest("20:00", "22:00"),
            1, 9, "管理员", CancellationToken.None);

        Assert.True(result.Id > 0);
        Assert.Equal(new TimeSpan(20, 0, 0), result.StartTime);
        Assert.Equal(new TimeSpan(22, 0, 0), result.EndTime);
        Assert.Contains(_audit.Entries, e => e.ActionType == "CREATE_PEAK_HOUR");
    }

    [Theory]
    [InlineData("abc", "22:00")]
    [InlineData("22:00", "20:00")]      // 开始晚于结束
    [InlineData("20:15", "22:00")]      // 非 30 分钟对齐
    [InlineData("20:00", "20:00")]      // 相同
    public async Task CreateAsync_InvalidRange_Throws(string start, string end)
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new PeakHourUpsertRequest(start, end), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_PEAK_HOUR", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_OverlappingRange_Throws()
    {
        var db = _factory.CreateDbContext();
        db.PeakRestrictedHours.Add(new PeakRestrictedHourEntity
        {
            StoreId = 1, StartTime = new TimeSpan(20, 0, 0), EndTime = new TimeSpan(22, 0, 0), Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new PeakHourUpsertRequest("21:00", "23:00"), 1, 9, "a", CancellationToken.None));
        Assert.Equal("PEAK_HOUR_OVERLAP", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_MoreThanTenActive_Throws()
    {
        var db = _factory.CreateDbContext();
        for (var i = 0; i < 10; i++)
        {
            db.PeakRestrictedHours.Add(new PeakRestrictedHourEntity
            {
                StoreId = 1, StartTime = new TimeSpan(i, 0, 0), EndTime = new TimeSpan(i, 30, 0), Status = 1,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var service = CreateService();
        // 已存在 10 条活跃配置；新时段有效且不重叠 → 触发数量上限
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(new PeakHourUpsertRequest("11:30", "12:00"), 1, 9, "a", CancellationToken.None));
        Assert.Equal("TOO_MANY_PEAK_HOURS", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_ChangesRangeAndAudits()
    {
        var db = _factory.CreateDbContext();
        var entity = new PeakRestrictedHourEntity
        {
            StoreId = 1, StartTime = new TimeSpan(20, 0, 0), EndTime = new TimeSpan(22, 0, 0), Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.PeakRestrictedHours.Add(entity);
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.UpdateAsync(entity.Id, new PeakHourUpsertRequest("19:00", "20:00"), 1, 9, "管理员", CancellationToken.None);

        Assert.Equal(new TimeSpan(19, 0, 0), result.StartTime);
        Assert.Contains(_audit.Entries, e => e.ActionType == "UPDATE_PEAK_HOUR");
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(999, new PeakHourUpsertRequest("19:00", "20:00"), 1, 9, "a", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RemovesAndAudits()
    {
        var db = _factory.CreateDbContext();
        var entity = new PeakRestrictedHourEntity
        {
            StoreId = 1, StartTime = new TimeSpan(20, 0, 0), EndTime = new TimeSpan(22, 0, 0), Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.PeakRestrictedHours.Add(entity);
        await db.SaveChangesAsync();

        var service = CreateService();
        await service.DeleteAsync(entity.Id, 1, 9, "管理员", CancellationToken.None);

        var list = await service.ListAsync(1, CancellationToken.None);
        Assert.Empty(list);
        Assert.Contains(_audit.Entries, e => e.ActionType == "DELETE_PEAK_HOUR");
    }
}