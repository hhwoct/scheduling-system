namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class ScheduleResultEntity
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long StoreId { get; set; }

    public long EmployeeId { get; set; }

    public DateOnly WorkDate { get; set; }

    public long? ShiftTemplateId { get; set; }

    public TimeSpan TimeSlot { get; set; }

    public long? WorkstationId { get; set; }

    public int SkillScore { get; set; }

    public string Status { get; set; } = "DRAFT";

    public int Version { get; set; } = 1;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
