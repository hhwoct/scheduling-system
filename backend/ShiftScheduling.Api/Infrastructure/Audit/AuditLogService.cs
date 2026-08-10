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
        // 使用独立的 DbContext 写入审计日志，避免与业务事务共用 ChangeTracker
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
            CreatedAt = DateTime.Now
        });

        await auditDbContext.SaveChangesAsync(cancellationToken);
    }
}
