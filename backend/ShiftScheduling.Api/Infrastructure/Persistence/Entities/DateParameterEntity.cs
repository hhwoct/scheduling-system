namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class DateParameterEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public DateOnly WorkDate { get; set; }

    public int WeekDay { get; set; }

    public string DayType { get; set; } = string.Empty;

    public int IsLegalHoliday { get; set; }

    public int IsHolidayEve { get; set; }

    public DateTime CreatedAt { get; set; }
}
