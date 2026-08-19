using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Tests.TestInfra;

/// <summary>
/// 内存审计日志：记录每次写入，便于断言服务是否触发审计。
/// </summary>
public sealed class FakeAuditLogService : IAuditLogService
{
    public sealed record AuditEntry(
        long StoreId,
        long? OperatorUserId,
        string? OperatorName,
        string ActionType,
        string TargetType,
        long? TargetId,
        string? BeforeContent,
        string? AfterContent,
        string? Remark);

    public List<AuditEntry> Entries { get; } = new();

    public Task WriteAsync(
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
        Entries.Add(new AuditEntry(storeId, operatorUserId, operatorName, actionType, targetType, targetId, beforeContent, afterContent, remark));
        return Task.CompletedTask;
    }

    public Task WriteInTransactionAsync(
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
        Entries.Add(new AuditEntry(storeId, operatorUserId, operatorName, actionType, targetType, targetId, beforeContent, afterContent, remark));
        return Task.CompletedTask;
    }

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
        Entries.Add(new AuditEntry(storeId, operatorUserId, operatorName, actionType, targetType, targetId, beforeContent, afterContent, remark));
    }
}
