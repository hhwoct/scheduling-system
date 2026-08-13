namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class EmployeeEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string Department { get; set; } = string.Empty;

    public DateTime? HireDate { get; set; }

    public string? PrimaryPosition { get; set; }

    public decimal MaxWeeklyHours { get; set; } = 48;

    public int Status { get; set; }

    public int IsParttime { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}