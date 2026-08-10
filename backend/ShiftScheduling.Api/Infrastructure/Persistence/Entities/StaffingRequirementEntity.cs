namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class StaffingRequirementEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string DayType { get; set; } = string.Empty;

    public long WorkstationId { get; set; }

    public TimeSpan TimeSlot { get; set; }

    public int RequiredCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
