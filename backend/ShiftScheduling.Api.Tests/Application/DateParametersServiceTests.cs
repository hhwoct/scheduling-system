using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.DateParameters;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>日期参数（节假日/工作日配置）服务单元测试。</summary>
public sealed class DateParametersServiceTests
{
    private readonly InMemoryTestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private DateParametersService CreateService() => new(_factory.CreateDbContext(), _audit);

    [Fact]
    public async Task GetMonthAsync_InvalidMonth_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetMonthAsync(1, 2026, 13, CancellationToken.None));
        Assert.Equal("INVALID_MONTH", ex.ErrorCode);
    }

    [Fact]
    public async Task GetMonthAsync_UnconfiguredDates_ReturnSuggestedWeekendRule()
    {
        var service = CreateService();

        // 2026-10-01 周四 ~ 2026-10-07 周三（未配置时按规则建议）
        var result = await service.GetMonthAsync(1, 2026, 10, CancellationToken.None);

        Assert.Equal(31, result.Items.Count);
        Assert.Equal("2026-10-01", result.Items[0].WorkDate.ToString("yyyy-MM-dd"));
        Assert.False(result.Items[0].IsConfigured);
        Assert.Equal("WORKDAY", result.Items[0].DayType);            // 周四

        // 周五/周六 → WEEKEND 建议，周日 → WORKDAY
        var friday = result.Items.First(x => x.WorkDate.Day == 2);   // 2026-10-02 周五
        Assert.Equal("WEEKEND", friday.DayType);
        var saturday = result.Items.First(x => x.WorkDate.Day == 3); // 2026-10-03 周六
        Assert.Equal("WEEKEND", saturday.DayType);
        var sunday = result.Items.First(x => x.WorkDate.Day == 4);   // 2026-10-04 周日
        Assert.Equal("WORKDAY", sunday.DayType);
    }

    [Fact]
    public async Task GetMonthAsync_ConfiguredDates_ReturnDatabaseValue()
    {
        var db = _factory.CreateDbContext();
        db.DateParameters.Add(new DateParameterEntity
        {
            StoreId = 1,
            WorkDate = new DateOnly(2026, 10, 1),
            WeekDay = 5,
            DayType = "HOLIDAY",
            IsLegalHoliday = 1,
            IsHolidayEve = 0,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.GetMonthAsync(1, 2026, 10, CancellationToken.None);

        var first = result.Items[0];
        Assert.True(first.IsConfigured);
        Assert.Equal("HOLIDAY", first.DayType);
        Assert.Equal(1, first.IsLegalHoliday);
        Assert.Equal(5, first.WeekDay);
    }

    [Fact]
    public async Task SaveAsync_EmptyItems_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(1, [], 1, "admin", CancellationToken.None));
        Assert.Equal("INVALID_REQUEST", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_InvalidDateOrDayType_Throws()
    {
        var service = CreateService();

        var badDate = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(1, [new DateParameterEntry("2026/10/01", "WORKDAY")], 1, "admin", CancellationToken.None));
        Assert.Equal("INVALID_WORK_DATE", badDate.ErrorCode);

        var badType = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(1, [new DateParameterEntry("2026-10-01", "FESTIVAL")], 1, "admin", CancellationToken.None));
        Assert.Equal("INVALID_DAY_TYPE", badType.ErrorCode);

        var dup = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(1, [new DateParameterEntry("2026-10-01", "WORKDAY"), new DateParameterEntry("2026-10-01", "HOLIDAY")], 1, "admin", CancellationToken.None));
        Assert.Equal("DUPLICATE_WORK_DATE", dup.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_UpsertNewAndUpdate_PersistsAndAudits()
    {
        // 预置一条 10-01 为 WORKDAY
        var db = _factory.CreateDbContext();
        db.DateParameters.Add(new DateParameterEntity
        {
            StoreId = 1,
            WorkDate = new DateOnly(2026, 10, 1),
            WeekDay = 5,
            DayType = "WORKDAY",
            IsLegalHoliday = 0,
            IsHolidayEve = 0,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.SaveAsync(1,
        [
            new DateParameterEntry("2026-10-01", "HOLIDAY", 1),            // 更新
            new DateParameterEntry("2026-10-02", "WEEKEND"),               // 新增（周五）
            new DateParameterEntry("2026-10-10", "WORKDAY"),               // 新增（调休补班）
        ], 1, "admin", CancellationToken.None);

        Assert.Equal(2, result.NewCount);
        Assert.Equal(1, result.UpdatedCount);

        var verify = _factory.CreateDbContext();
        var saved = await verify.DateParameters
            .Where(x => x.StoreId == 1 && x.WorkDate >= new DateOnly(2026, 10, 1) && x.WorkDate <= new DateOnly(2026, 10, 10))
            .OrderBy(x => x.WorkDate)
            .ToListAsync();

        Assert.Equal(3, saved.Count);
        Assert.Equal("HOLIDAY", saved[0].DayType);
        Assert.Equal(1, saved[0].IsLegalHoliday);
        Assert.Equal(6, saved[1].WeekDay);     // 周五 → MySQL DAYOFWEEK=6
        Assert.Equal("WORKDAY", saved[2].DayType);
        Assert.Equal(7, saved[2].WeekDay);     // 周六补班日

        Assert.Single(_audit.Entries);
        Assert.Equal("SAVE_DATE_PARAMETERS", _audit.Entries[0].ActionType);
        Assert.Contains("新增 2", _audit.Entries[0].Remark);
    }

    [Fact]
    public async Task GenerateAsync_OnlyInsertsMissingDates_AndKeepsManualConfig()
    {
        // 预置"下月 1 号"为 HOLIDAY（人工配置，如元旦），生成时不得覆盖
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);

        var db = _factory.CreateDbContext();
        db.DateParameters.Add(new DateParameterEntity
        {
            StoreId = 1,
            WorkDate = nextMonth,
            WeekDay = (int)nextMonth.DayOfWeek + 1,
            DayType = "HOLIDAY",
            IsLegalHoliday = 1,
            IsHolidayEve = 0,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.GenerateAsync(1, 3, 1, "admin", CancellationToken.None);

        var end = nextMonth.AddMonths(3).AddDays(-1);
        var expectedDays = end.DayNumber - nextMonth.DayNumber + 1;
        Assert.Equal(expectedDays - 1, result.Inserted);   // 除预置 1 天外全部插入
        Assert.Equal(nextMonth, result.StartDate);
        Assert.Equal(end, result.EndDate);

        var verify = _factory.CreateDbContext();
        var first = await verify.DateParameters.FirstAsync(x => x.StoreId == 1 && x.WorkDate == nextMonth);
        Assert.Equal("HOLIDAY", first.DayType);           // 人工配置未被覆盖

        // 生成范围内某个周五应为 WEEKEND
        var friday = nextMonth.AddDays(1);
        while (friday.DayOfWeek != DayOfWeek.Friday)
        {
            friday = friday.AddDays(1);
        }

        var fridayRow = await verify.DateParameters.FirstAsync(x => x.StoreId == 1 && x.WorkDate == friday);
        Assert.Equal("WEEKEND", fridayRow.DayType);

        Assert.Single(_audit.Entries);
        Assert.Equal("GENERATE_DATE_PARAMETERS", _audit.Entries[0].ActionType);
    }

    [Fact]
    public async Task GenerateAsync_InvalidMonths_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateAsync(1, 13, 1, "admin", CancellationToken.None));
        Assert.Equal("INVALID_MONTHS", ex.ErrorCode);
    }
}
