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

    Task PublishAsync(
        long planId,
        long storeId,
        long operatorUserId,
        string operatorName,
        bool force,
        CancellationToken cancellationToken);
}
