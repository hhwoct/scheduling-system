namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class ShiftWorkstationEntity
{
    public long Id { get; set; }

    public long ShiftTemplateId { get; set; }

    public long WorkstationId { get; set; }

    public DateTime CreatedAt { get; set; }
}
