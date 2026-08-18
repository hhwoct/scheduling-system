namespace ShiftScheduling.Api.Application.Ai;

public interface IAiConfigService
{
    Task<AiConfigItem> GetAsync(long storeId, CancellationToken cancellationToken);

    Task<AiConfigItem> SaveAsync(AiConfigSaveRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    /// <summary>取生效配置（未配置 Key 时抛业务异常）。</summary>
    Task<(string ApiKey, string BaseUrl, string Model)> GetEffectiveAsync(long storeId, CancellationToken cancellationToken);
}
