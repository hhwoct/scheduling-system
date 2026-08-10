namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class WorkstationEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string? Remark { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}