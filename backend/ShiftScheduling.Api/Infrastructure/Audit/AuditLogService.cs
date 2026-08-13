using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Infrastructure.Audit;

public sealed class AuditLogService : IAuditLogService
{
    private readonly IDbContextFactory<ShiftSchedulingDbContext> _dbContextFactory;

    public AuditLogService(IDbContextFactory<ShiftSchedulingDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task WriteAsync(
        long storeId,
        long? operatorUserId,
        string? operatorName,
        string actionType,
        string targetType,
        long? targetId,
        string? beforeContent,
        string? afterContent,
        string? remark,
        CancellationToken cancellationToken)
    {
        await using var auditDbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        auditDbContext.AuditLogs.Add(new AuditLogEntity
        {
            StoreId = storeId,
            OperatorUserId = operatorUserId,
            OperatorName = operatorName,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            BeforeContent = beforeContent,
            AfterContent = afterContent,
            Remark = remark,
            CreatedAt = DateTime.UtcNow
        });

        await auditDbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// P1-7 修复：在业务事务内写入审计日志。
    /// </summary>
    public async Task WriteInTransactionAsync(
        long storeId,
        long? operatorUserId,
        string? operatorName,
        string actionType,
        string targetType,
        long? targetId,
        string? beforeContent,
        string? afterContent,
        string? remark,
        CancellationToken cancellationToken)
    {
        await using var auditDbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        auditDbContext.AuditLogs.Add(new AuditLogEntity
        {
            StoreId = storeId,
            OperatorUserId = operatorUserId,
            OperatorName = operatorName,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            BeforeContent = beforeContent,
            AfterContent = afterContent,
            Remark = remark,
            CreatedAt = DateTime.UtcNow
        });

        await auditDbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// P1-7 修复：将审计实体加入业务 DbContext 的 ChangeTracker。
    /// 与业务操作在同一个 SaveChanges 中一并提交，保证事务原子性。
    /// </summary>
    public void AddAuditEntity(
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
        DateTime createdAt)
    {
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            StoreId = storeId,
            OperatorUserId = operatorUserId,
            OperatorName = operatorName,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            BeforeContent = beforeContent,
            AfterContent = afterContent,
            Remark = remark,
            CreatedAt = createdAt
        });
    }
}