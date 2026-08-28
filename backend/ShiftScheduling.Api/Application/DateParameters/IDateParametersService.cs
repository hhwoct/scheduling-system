using ShiftScheduling.Api.Application.DateParameters;

namespace ShiftScheduling.Api.Application.DateParameters;

/// <summary>日期参数（节假日/工作日配置）服务。</summary>
public interface IDateParametersService
{
    /// <summary>查询某月（1 号~月末）逐日配置；未配置的日期返回按规则的建议值并标记 IsConfigured=false。</summary>
    Task<DateParameterMonth> GetMonthAsync(long storeId, int year, int month, CancellationToken cancellationToken);

    /// <summary>批量保存日期配置（按 store_id + work_date 幂等 upsert）。</summary>
    Task<DateParameterSaveResult> SaveAsync(
        long storeId,
        List<DateParameterEntry> items,
        long? operatorUserId,
        string? operatorName,
        CancellationToken cancellationToken);

    /// <summary>按周末规则补全从下月起连续的 months 个月；已配置的日期不覆盖（保留人工设置）。</summary>
    Task<DateParameterGenerateResult> GenerateAsync(
        long storeId,
        int months,
        long? operatorUserId,
        string? operatorName,
        CancellationToken cancellationToken);
}
