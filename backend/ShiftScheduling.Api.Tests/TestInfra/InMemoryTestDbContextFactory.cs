using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Tests.TestInfra;

/// <summary>
/// 测试用内存 DbContext 工厂（EF InMemory）。
/// 用于含 TimeSpan 比较/排序查询的服务（SQLite 无法翻译 TimeSpan 表达式）。
/// </summary>
public sealed class InMemoryTestDbContextFactory : IDbContextFactory<ShiftSchedulingDbContext>, IDisposable
{
    private readonly string _dbName = "test-" + Guid.NewGuid().ToString("N");

    public ShiftSchedulingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ShiftSchedulingDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new ShiftSchedulingDbContext(options);
    }

    public Task<ShiftSchedulingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    public void Dispose()
    {
    }
}
