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
}
