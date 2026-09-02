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
        long? storeId,
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

    Task<DailyViewResult> GetDailyViewAsync(
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

    /// <summary>取消发布：已发布计划退回草稿（PublishedAt 清空、明细回 DRAFT），并通知员工。</summary>
    Task UnpublishAsync(
        long planId,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>空位加人：给员工新增一个 30 分钟上班段（草稿/已发布均可；已发布时通知员工）。</summary>
    Task<AddScheduleSlotRequest> AddSlotAsync(
        long planId,
        AddScheduleSlotRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>空位加人候选员工列表（技能/兼职岗位限制/请假/当天已排班过滤）。</summary>
    Task<IReadOnlyList<AddSlotCandidateItem>> GetAddSlotCandidatesAsync(
        long planId,
        long storeId,
        DateOnly workDate,
        TimeSpan timeSlot,
        long workstationId,
        CancellationToken cancellationToken);

    /// <summary>移除空位加人的上班段（撤回操作）：删除指定时段明细并按剩余时段重算汇总。</summary>
    Task RemoveSlotAsync(
        long planId,
        AddScheduleSlotRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>换人：移除范围内全部员工的时段，改为所选员工（仅草稿计划）。</summary>
    Task ReplaceSlotAsync(
        long planId,
        AddScheduleSlotRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>范围平移：所选时段内全部明细整体 ±30 分钟平移（仅草稿计划）。</summary>
    Task<int> MoveRangeAsync(
        long planId,
        MoveScheduleRangeRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>取消排班：删除所选时段内全部明细（直接下班），重算受影响员工汇总（仅草稿计划）。</summary>
    Task<int> ClearRangeAsync(
        long planId,
        ClearScheduleRangeRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);
}
