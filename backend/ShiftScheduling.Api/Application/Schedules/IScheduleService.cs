using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.Schedules;

public interface IScheduleService
{
    Task<GenerateScheduleResult> GenerateAsync(
        GenerateScheduleRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    Task<PagedResult<SchedulePlanItem>> ListPlansAsync(
        int page,
        int pageSize,
        long storeId,
        string? status,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MonthViewItem>> GetMonthViewAsync(
        long planId,
        long storeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WeekViewItem>> GetWeekViewAsync(
        long planId,
        long storeId,
        DateOnly? weekStart,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DailyViewItem>> GetDailyViewAsync(
        long planId,
        long storeId,
        DateOnly workDate,
        CancellationToken cancellationToken);

    Task<ScheduleSummaryDto> GetSummaryAsync(long planId, long storeId, CancellationToken cancellationToken);

    Task AdjustAsync(
        long planId,
        AdjustScheduleRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    Task SetDayStatusAsync(
        long planId,
        SetDayStatusRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    Task SetSlotStatusAsync(
        long planId,
        SetSlotStatusRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>拖动移动连续工作段（时间平移 + 换工作站）。</summary>
    Task<MoveScheduleSegmentResult> MoveSegmentAsync(
        long planId,
        MoveScheduleSegmentRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>复制上周（P2）：最近一期已发布计划按星期几映射复制到目标草稿计划。</summary>
    Task CopyPreviousAsync(
        long planId,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    Task PublishAsync(
        long planId,
        long storeId,
        long operatorUserId,
        string operatorName,
        bool force,
        CancellationToken cancellationToken);
}
