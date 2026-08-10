namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class ScheduleIssueEntity
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long StoreId { get; set; }

    public string IssueType { get; set; } = string.Empty;

    public string Severity { get; set; } = "WARN";

    public DateOnly? WorkDate { get; set; }

    public TimeSpan? TimeSlot { get; set; }

    public long? EmployeeId { get; set; }

    public long? WorkstationId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Status { get; set; } = "OPEN";

    public DateTime CreatedAt { get; set; }
}
