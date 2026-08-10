namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class ShiftSwapEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public long PlanId { get; set; }

    public long RequesterEmployeeId { get; set; }

    public long TargetEmployeeId { get; set; }

    public DateOnly SwapDate { get; set; }

    public string? Reason { get; set; }

    public string Status { get; set; } = "PENDING";

    public long? ReviewUserId { get; set; }

    public DateTime? ReviewTime { get; set; }

    public string? ReviewRemark { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}