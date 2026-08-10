namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class ScheduleSummaryEntity
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long StoreId { get; set; }

    public long EmployeeId { get; set; }

    public DateOnly WorkDate { get; set; }

    public int IsRestDay { get; set; }

    public long? ShiftTemplateId { get; set; }

    public TimeSpan? StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }

    public decimal WorkHours { get; set; }

    public string? CoveredWorkstations { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
