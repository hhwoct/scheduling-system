using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Tests.TestInfra;

/// <summary>
/// 测试用 DbContext 工厂：共享一个打开的内存 SQLite 连接，
/// 使不同 DbContext 实例看到同一份数据（模拟真实数据库会话）。
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<ShiftSchedulingDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var bootstrap = CreateDbContext();
        bootstrap.Database.EnsureCreated();
    }

    public ShiftSchedulingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ShiftSchedulingDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ShiftSchedulingDbContext(options);
    }

    public Task<ShiftSchedulingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}
