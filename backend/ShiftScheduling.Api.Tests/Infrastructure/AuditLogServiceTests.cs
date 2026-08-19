using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Infrastructure;

/// <summary>审计日志服务单元测试。</summary>
public sealed class AuditLogServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task WriteAsync_PersistsAuditEntry()
    {
        var service = new AuditLogService(_factory);
        await service.WriteAsync(
            1, 9, "管理员", "TEST_ACTION", "TEST_TARGET", 42,
            "before", "after", "测试备注", CancellationToken.None);

        var db = _factory.CreateDbContext();
        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();

        Assert.Equal(1, entry.StoreId);
        Assert.Equal(9, entry.OperatorUserId);
        Assert.Equal("TEST_ACTION", entry.ActionType);
        Assert.Equal("TEST_TARGET", entry.TargetType);
        Assert.Equal(42, entry.TargetId);
        Assert.Equal("before", entry.BeforeContent);
        Assert.Equal("after", entry.AfterContent);
        Assert.Equal("测试备注", entry.Remark);
    }

    [Fact]
    public async Task WriteInTransactionAsync_PersistsEntry()
    {
        var service = new AuditLogService(_factory);
        await service.WriteInTransactionAsync(
            2, null, null, "TX_ACTION", "TX_TARGET", null,
            null, null, null, CancellationToken.None);

        var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public void AddAuditEntity_AddsToChangeTracker_NotSavedUntilSaveChanges()
    {
        var db = _factory.CreateDbContext();
        var service = new AuditLogService(_factory);

        service.AddAuditEntity(
            db, 3, 7, "操作员", "INLINE_ACTION", "INLINE_TARGET", 1,
            null, "{}", "内联审计", DateTime.UtcNow);

        // 尚未 SaveChanges：数据库无记录，但 ChangeTracker 有实体
        Assert.Equal(0, db.AuditLogs.IgnoreQueryFilters().Count());

        db.SaveChanges();
        Assert.Equal(1, db.AuditLogs.Count());
    }
}
