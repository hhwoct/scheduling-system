namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class EmployeeSkillEntity
{
    public long Id { get; set; }

    public long EmployeeId { get; set; }

    public long WorkstationId { get; set; }

    public int SkillScore { get; set; }

    public int IsPrimarySkill { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
