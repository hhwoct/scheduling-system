namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class StoreEntity
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public int MaxEmployeeCount { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
