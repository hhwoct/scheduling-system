namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class ShiftTemplateEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public int IsCrossDay { get; set; }

    public int Priority { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
