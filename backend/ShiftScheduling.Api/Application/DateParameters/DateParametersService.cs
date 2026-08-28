using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.DateParameters;

/// <summary>
/// 日期参数（节假日/工作日配置）服务。
/// 口径与 init_shift_mvp.sql 一致：周五/周六 = WEEKEND（晚市高峰日），周日~周四 = WORKDAY；
/// 法定节假日（国庆/中秋等）需人工标记为 HOLIDAY；调休补班日人工改为 WORKDAY。
/// </summary>
public sealed class DateParametersService : IDateParametersService
{
    private const int MaxEntriesPerSave = 400;

    private static readonly string[] ValidDayTypes = ["WORKDAY", "WEEKEND", "HOLIDAY"];

    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public DateParametersService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<DateParameterMonth> GetMonthAsync(
        long storeId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
        {
            throw new BusinessException("月份参数无效（year 2000~2100，month 1~12）", "INVALID_MONTH");
        }

        var dayCount = DateTime.DaysInMonth(year, month);
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, dayCount);

        var existing = await _dbContext.DateParameters
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.WorkDate >= start && x.WorkDate <= end)
            .Select(x => new { x.WorkDate, x.WeekDay, x.DayType, x.IsLegalHoliday, x.IsHolidayEve })
            .ToDictionaryAsync(x => x.WorkDate, cancellationToken);

        var items = new List<DateParameterItem>(dayCount);
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (existing.TryGetValue(d, out var row))
            {
                items.Add(new DateParameterItem(d, row.WeekDay, row.DayType, row.IsLegalHoliday, row.IsHolidayEve, true));
            }
            else
            {
                items.Add(new DateParameterItem(d, ToMySqlWeekDay(d), SuggestDayType(d), 0, 0, false));
            }
        }

        return new DateParameterMonth(year, month, items);
    }

    public async Task<DateParameterSaveResult> SaveAsync(
        long storeId,
        List<DateParameterEntry> items,
        long? operatorUserId,
        string? operatorName,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new BusinessException("保存项不能为空", "INVALID_REQUEST");
        }

        if (items.Count > MaxEntriesPerSave)
        {
            throw new BusinessException($"单次保存最多 {MaxEntriesPerSave} 条", "TOO_MANY_ITEMS");
        }

        var parsed = new List<(DateOnly WorkDate, string DayType, int IsLegalHoliday, int IsHolidayEve)>(items.Count);
        var seen = new HashSet<DateOnly>();
        foreach (var item in items)
        {
            if (!DateOnly.TryParseExact(item.WorkDate, "yyyy-MM-dd", out var workDate))
            {
                throw new BusinessException($"日期格式无效：{item.WorkDate}（应为 yyyy-MM-dd）", "INVALID_WORK_DATE");
            }

            if (!ValidDayTypes.Contains(item.DayType))
            {
                throw new BusinessException("日期类型无效，仅支持 WORKDAY/WEEKEND/HOLIDAY", "INVALID_DAY_TYPE");
            }

            if (item.IsLegalHoliday is not (0 or 1) || item.IsHolidayEve is not (0 or 1))
            {
                throw new BusinessException("法定节假日/节前日标记仅支持 0/1", "INVALID_FLAG");
            }

            if (!seen.Add(workDate))
            {
                throw new BusinessException($"同一天重复提交：{item.WorkDate}", "DUPLICATE_WORK_DATE");
            }

            parsed.Add((workDate, item.DayType, item.IsLegalHoliday, item.IsHolidayEve));
        }

        var workDates = parsed.Select(x => x.WorkDate).ToList();
        var existing = await _dbContext.DateParameters
            .Where(x => x.StoreId == storeId && workDates.Contains(x.WorkDate))
            .ToDictionaryAsync(x => x.WorkDate, cancellationToken);

        var newCount = 0;
        var updatedCount = 0;
        foreach (var (workDate, dayType, isLegalHoliday, isHolidayEve) in parsed)
        {
            if (existing.TryGetValue(workDate, out var row))
            {
                row.DayType = dayType;
                row.IsLegalHoliday = isLegalHoliday;
                row.IsHolidayEve = isHolidayEve;
                updatedCount++;
            }
            else
            {
                _dbContext.DateParameters.Add(new DateParameterEntity
                {
                    StoreId = storeId,
                    WorkDate = workDate,
                    WeekDay = ToMySqlWeekDay(workDate),
                    DayType = dayType,
                    IsLegalHoliday = isLegalHoliday,
                    IsHolidayEve = isHolidayEve,
                    CreatedAt = DateTime.UtcNow,
                });
                newCount++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "SAVE_DATE_PARAMETERS",
            "DATE_PARAMETER",
            null,
            null,
            null,
            $"保存 {parsed.Count} 条日期参数（新增 {newCount}，更新 {updatedCount}，范围 {parsed.Min(x => x.WorkDate):yyyy-MM-dd} ~ {parsed.Max(x => x.WorkDate):yyyy-MM-dd}）",
            cancellationToken);

        return new DateParameterSaveResult(newCount, updatedCount);
    }

    public async Task<DateParameterGenerateResult> GenerateAsync(
        long storeId,
        int months,
        long? operatorUserId,
        string? operatorName,
        CancellationToken cancellationToken)
    {
        if (months < 1 || months > 12)
        {
            throw new BusinessException("补全月数无效（1~12）", "INVALID_MONTHS");
        }

        // 从下个月起连续补全 months 个月（周五/周六 = WEEKEND，其余 = WORKDAY）
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var end = start.AddMonths(months).AddDays(-1);

        var existing = await _dbContext.DateParameters
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.WorkDate >= start && x.WorkDate <= end)
            .Select(x => x.WorkDate)
            .ToHashSetAsync(cancellationToken);

        var inserted = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (existing.Contains(d))
            {
                continue;
            }

            _dbContext.DateParameters.Add(new DateParameterEntity
            {
                StoreId = storeId,
                WorkDate = d,
                WeekDay = ToMySqlWeekDay(d),
                DayType = SuggestDayType(d),
                IsLegalHoliday = 0,
                IsHolidayEve = 0,
                CreatedAt = DateTime.UtcNow,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "GENERATE_DATE_PARAMETERS",
            "DATE_PARAMETER",
            null,
            null,
            null,
            $"按周末规则补全 {months} 个月（{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}），新增 {inserted} 条，已配置日期不覆盖",
            cancellationToken);

        return new DateParameterGenerateResult(inserted, start, end);
    }

    /// <summary>MySQL DAYOFWEEK 语义：1=周日 … 7=周六。</summary>
    private static int ToMySqlWeekDay(DateOnly date) => (int)date.DayOfWeek + 1;

    /// <summary>建议口径：周五/周六 = WEEKEND，其余 = WORKDAY。</summary>
    private static string SuggestDayType(DateOnly date)
        => date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? "WEEKEND" : "WORKDAY";
}
