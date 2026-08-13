using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Infrastructure.Audit;

public interface IAuditLogService
{
    Task WriteAsync(
        long storeId,
        long? operatorUserId,
        string? operatorName,
        string actionType,
        string targetType,
        long? targetId,
        string? beforeContent,
        string? afterContent,
        string? remark,
        CancellationToken cancellationToken);

    /// <summary>
    /// P1-7 修复：在业务事务内写入审计日志。
    /// 业务 Service 先在事务中执行业务操作 + 审计写入，再提交事务。
    /// </summary>
    Task WriteInTransactionAsync(
        long storeId,
        long? operatorUserId,
        string? operatorName,
        string actionType,
        string targetType,
        long? targetId,
        string? beforeContent,
        string? afterContent,
        string? remark,
        CancellationToken cancellationToken);

    /// <summary>
    /// P1-7 修复：将审计实体加入业务 DbContext 的 ChangeTracker。
    /// 与业务操作在同一个 SaveChanges 中一并提交，保证事务原子性。
    /// 调用方必须在事务中调用本方法。
    /// </summary>
    void AddAuditEntity(
        ShiftSchedulingDbContext dbContext,
        long storeId,
        long? operatorUserId,
        string? operatorName,
        string actionType,
        string targetType,
        long? targetId,
        string? beforeContent,
        string? afterContent,
        string? remark,
        DateTime createdAt);
}