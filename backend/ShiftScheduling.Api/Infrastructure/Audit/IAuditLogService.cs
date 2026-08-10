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
}
