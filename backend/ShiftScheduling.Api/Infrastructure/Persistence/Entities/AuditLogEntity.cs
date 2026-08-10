namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class AuditLogEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public long? OperatorUserId { get; set; }

    public string? OperatorName { get; set; }

    public string ActionType { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public long? TargetId { get; set; }

    public string? BeforeContent { get; set; }

    public string? AfterContent { get; set; }

    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; }
}
