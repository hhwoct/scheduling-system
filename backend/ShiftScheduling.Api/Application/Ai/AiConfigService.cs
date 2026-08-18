using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.Ai;

public sealed class AiConfigService : IAiConfigService
{
    private const string Provider = "DEEPSEEK";
    private const string DefaultBaseUrl = "https://api.deepseek.com";
    private const string DefaultModel = "deepseek-chat";

    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public AiConfigService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<AiConfigItem> GetAsync(long storeId, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.AiConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Provider == Provider, cancellationToken);

        if (entity is null)
        {
            return new AiConfigItem(false, Provider, DefaultBaseUrl, DefaultModel, string.Empty);
        }

        return new AiConfigItem(
            !string.IsNullOrWhiteSpace(entity.ApiKey),
            entity.Provider,
            string.IsNullOrWhiteSpace(entity.BaseUrl) ? DefaultBaseUrl : entity.BaseUrl,
            string.IsNullOrWhiteSpace(entity.Model) ? DefaultModel : entity.Model,
            MaskKey(entity.ApiKey));
    }

    public async Task<AiConfigItem> SaveAsync(
        AiConfigSaveRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? DefaultBaseUrl : request.BaseUrl.Trim();
        var model = string.IsNullOrWhiteSpace(request.Model) ? DefaultModel : request.Model.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            throw new BusinessException("接口地址必须是 https 开头的完整地址", "INVALID_AI_BASE_URL");
        }

        if (model.Length > 80)
        {
            throw new BusinessException("模型名称过长（最多 80 字符）", "INVALID_AI_MODEL");
        }

        var entity = await _dbContext.AiConfigs
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Provider == Provider, cancellationToken);

        if (entity is null)
        {
            entity = new AiConfigEntity
            {
                StoreId = storeId,
                Provider = Provider,
                Status = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.AiConfigs.Add(entity);
        }

        var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? entity.ApiKey : request.ApiKey.Trim();
        if (apiKey.Length > 255)
        {
            throw new BusinessException("API Key 过长（最多 255 字符）", "INVALID_AI_API_KEY");
        }

        entity.ApiKey = apiKey;
        entity.BaseUrl = baseUrl;
        entity.Model = model;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "SAVE_AI_CONFIG",
            "AI_CONFIG",
            entity.Id,
            null,
            $"{Provider}: {baseUrl} / {model}",
            $"保存 AI 文档识别配置（{Provider}）",
            cancellationToken);

        return new AiConfigItem(!string.IsNullOrWhiteSpace(apiKey), Provider, baseUrl, model, MaskKey(apiKey));
    }

    public async Task<(string ApiKey, string BaseUrl, string Model)> GetEffectiveAsync(
        long storeId,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.AiConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Provider == Provider, cancellationToken);

        if (entity is null || string.IsNullOrWhiteSpace(entity.ApiKey))
        {
            throw new BusinessException("尚未配置 DeepSeek API Key，请先点击页面上方「AI 设置」填入 Key", "AI_NOT_CONFIGURED");
        }

        var baseUrl = string.IsNullOrWhiteSpace(entity.BaseUrl) ? DefaultBaseUrl : entity.BaseUrl;
        var model = string.IsNullOrWhiteSpace(entity.Model) ? DefaultModel : entity.Model;
        return (entity.ApiKey, baseUrl, model);
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (key.Length <= 8)
        {
            return key[..2] + "****";
        }

        return key[..4] + "****" + key[^4..];
    }
}
