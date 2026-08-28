namespace ShiftScheduling.Api.Application.DateParameters;

/// <summary>日期参数配置项（返回端）。未配置的日期 DayType 为按规则的建议值，IsConfigured=false。</summary>
public sealed record DateParameterItem(
    DateOnly WorkDate,
    int WeekDay,
    string DayType,
    int IsLegalHoliday,
    int IsHolidayEve,
    bool IsConfigured);

/// <summary>某月日期参数查询结果（1 号到月末逐日）。</summary>
public sealed record DateParameterMonth(
    int Year,
    int Month,
    List<DateParameterItem> Items);

/// <summary>单个日期保存项。</summary>
public sealed record DateParameterEntry(
    string WorkDate,
    string DayType,
    int IsLegalHoliday = 0,
    int IsHolidayEve = 0);

/// <summary>批量保存请求。</summary>
public sealed record DateParameterSaveRequest(List<DateParameterEntry> Items);

/// <summary>批量保存结果。</summary>
public sealed record DateParameterSaveResult(int NewCount, int UpdatedCount);

/// <summary>按周末规则补全请求（补全从下月起连续的 Months 个月）。</summary>
public sealed record DateParameterGenerateRequest(int Months);

/// <summary>按周末规则补全结果。</summary>
public sealed record DateParameterGenerateResult(int Inserted, DateOnly StartDate, DateOnly EndDate);
