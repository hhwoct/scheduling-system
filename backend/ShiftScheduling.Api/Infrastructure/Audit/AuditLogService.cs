using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Infrastructure.Audit;

public sealed class AuditLogService : IAuditLogService
{
    private readonly ShiftSchedulingDbContext _dbContext;

    public AuditLogService(ShiftSchedulingDbContext dbContext)
    {
        _dbContext = dbContext;
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
        _dbContext.AuditLogs.Add(new AuditLogEntity
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

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
