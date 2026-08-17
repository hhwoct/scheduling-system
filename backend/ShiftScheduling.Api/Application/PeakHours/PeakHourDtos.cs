namespace ShiftScheduling.Api.Application.PeakHours;

public sealed record PeakHourItem(
    long Id,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int Status);

/// <summary>新增/修改高峰时段请求；时间格式 HH:mm（30 分钟对齐）。</summary>
public sealed record PeakHourUpsertRequest(
    string StartTime,
    string EndTime,
    int Status = 1);
