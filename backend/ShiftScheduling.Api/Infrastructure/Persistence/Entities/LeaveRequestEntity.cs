namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class LeaveRequestEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public long EmployeeId { get; set; }

    public string LeaveType { get; set; } = "PERSONAL";

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string? Reason { get; set; }

    public string Status { get; set; } = "PENDING";

    public long? ReviewUserId { get; set; }

    public DateTime? ReviewTime { get; set; }

    public string? ReviewRemark { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}