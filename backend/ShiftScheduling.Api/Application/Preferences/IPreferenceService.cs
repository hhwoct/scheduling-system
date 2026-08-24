using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Application.Preferences;

public interface IPreferenceService
{
    /// <summary>全量重建偏好统计：历史已发布计划（不含最新一期）→ 偏好表；最新一期用于评估贴合率。</summary>
    Task<PreferenceStats> RebuildAsync(long storeId, CancellationToken cancellationToken);

    /// <summary>读取当前统计（不重建）。</summary>
    Task<PreferenceStats> GetStatsAsync(long storeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreferenceMatrixItem>> GetMatrixAsync(long storeId, string? dayType, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreferenceTopItem>> GetTopAsync(long storeId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreferenceTrendItem>> GetTrendsAsync(long storeId, CancellationToken cancellationToken);

    /// <summary>需求联动分析：聚合计划内调整明细（SET_WORK/MOVE_SEGMENT），
    /// 找出店长反复手动补人的 (日期类型, 时段, 工作站) 并给出需求配置建议。</summary>
    Task<IReadOnlyList<DemandInsightItem>> GetDemandInsightsAsync(long planId, long storeId, CancellationToken cancellationToken);
}
