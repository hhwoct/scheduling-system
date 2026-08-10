namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class SchedulePlanEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = "DRAFT";

    public long? CreatedBy { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
