using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.StaffingRequirements;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>人数需求服务单元测试。</summary>
public sealed class StaffingRequirementServiceTests
{
    private readonly InMemoryTestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private StaffingRequirementService CreateService()
        => new(_factory.CreateDbContext(), _audit);

    private static async Task<long> SeedWorkstationAsync(InMemoryTestDbContextFactory factory, string code = "WS01")
    {
        var db = factory.CreateDbContext();
        var ws = new WorkstationEntity
        {
            StoreId = 1, Code = code, Name = "工作站" + code, SortOrder = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Workstations.Add(ws);
        await db.SaveChangesAsync();
        return ws.Id;
    }

    [Fact]
    public async Task ListAsync_InvalidDayType_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ListAsync(1, "INVALID", CancellationToken.None));
        Assert.Equal("INVALID_DAY_TYPE", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_FullMatrixUpsert_PersistsAllSlots()
    {
        var wsId = await SeedWorkstationAsync(_factory);
        var service = CreateService();

        var result = await service.SaveAsync(
            new StaffingRequirementSaveRequest(
                "WORKDAY",
                new List<StaffingRequirementEntry>
                {
                    new(wsId, "18:00", 2, 3, null),
                    new(wsId, "18:30", 1, 2, "晚高峰加人")
                }),
            1, 9, "管理员", CancellationToken.None);

        // 全矩阵 48 格
        Assert.Equal(48, result.TotalSlots);
        Assert.Equal(2, result.NonZeroSlots);
        Assert.Contains(_audit.Entries, e => e.ActionType == "SAVE_STAFFING_REQUIREMENTS");
    }

    [Fact]
    public async Task SaveAsync_EmptyEntries_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(new StaffingRequirementSaveRequest("WORKDAY", new List<StaffingRequirementEntry>()), 1, 9, "a", CancellationToken.None));
        Assert.Equal("EMPTY_STAFFING_REQUIREMENT", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_InvalidDayType_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(
                new StaffingRequirementSaveRequest("BAD", new List<StaffingRequirementEntry> { new(1, "18:00", 1, 1, null) }),
                1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_DAY_TYPE", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_WorkstationNotInStore_Throws()
    {
        await SeedWorkstationAsync(_factory);
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(
                new StaffingRequirementSaveRequest("WORKDAY", new List<StaffingRequirementEntry> { new(999, "18:00", 1, 1, null) }),
                1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_WORKSTATION", ex.ErrorCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task SaveAsync_RequiredCountOutOfRange_Throws(int count)
    {
        var wsId = await SeedWorkstationAsync(_factory);
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(
                new StaffingRequirementSaveRequest("WORKDAY", new List<StaffingRequirementEntry> { new(wsId, "18:00", count, 0, null) }),
                1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_REQUIRED_COUNT", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_DuplicateSlot_Throws()
    {
        var wsId = await SeedWorkstationAsync(_factory);
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(
                new StaffingRequirementSaveRequest(
                    "WORKDAY",
                    new List<StaffingRequirementEntry>
                    {
                        new(wsId, "18:00", 1, 1, null),
                        new(wsId, "18:00", 2, 2, null)
                    }),
                1, 9, "a", CancellationToken.None));
        Assert.Equal("DUPLICATE_TIME_SLOT", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_InvalidSlotFormat_Throws()
    {
        var wsId = await SeedWorkstationAsync(_factory);
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(
                new StaffingRequirementSaveRequest("WORKDAY", new List<StaffingRequirementEntry> { new(wsId, "18:07", 1, 1, null) }),
                1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_TIME_SLOT", ex.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_FiltersByDayTypeAndJoinsWorkstationName()
    {
        var wsId = await SeedWorkstationAsync(_factory);
        var service = CreateService();
        await service.SaveAsync(
            new StaffingRequirementSaveRequest(
                "WORKDAY",
                new List<StaffingRequirementEntry> { new(wsId, "18:00", 2, 3, null) }),
            1, 9, "管理员", CancellationToken.None);

        var list = await service.ListAsync(1, "WORKDAY", CancellationToken.None);
        Assert.NotEmpty(list);
        Assert.Equal("工作站WS01", list[0].WorkstationName);
    }
}