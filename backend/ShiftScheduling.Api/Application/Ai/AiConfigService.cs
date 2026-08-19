using System.Net;
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
    private readonly IConfiguration _configuration;

    public AiConfigService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _configuration = configuration;
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

        // 3.5 修复：拒绝本机/内网地址，防止 BaseUrl 指向内部服务（SSRF）
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host, out var hostIp) && IsPrivateAddress(hostIp)))
        {
            throw new BusinessException("接口地址不能指向本机或内网地址", "INVALID_AI_BASE_URL");
        }

        // 安全：BaseUrl 仅允许 DeepSeek 官方域名（或通过 Ai:AllowedBaseUrlHosts 配置放行的域名），
        // 防止把存储的 API Key 与业务数据转发到攻击者控制的主机
        var extraAllowedHosts = _configuration["Ai:AllowedBaseUrlHosts"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        if (!IsOfficialDeepSeekHost(uri.Host) &&
            !extraAllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessException("接口地址仅支持 DeepSeek 官方域名（*.deepseek.com）或已配置的白名单域名", "INVALID_AI_BASE_URL");
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

        // 修复：审计与业务数据在同一事务内提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "SAVE_AI_CONFIG",
            "AI_CONFIG",
            entity.Id,
            null,
            $"{Provider}: {baseUrl} / {model}",
            $"保存 AI 文档识别配置（{Provider}）",
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

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

    private static bool IsOfficialDeepSeekHost(string host)
        => host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".deepseek.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);   // 链路本地，含云元数据地址 169.254.169.254
        }

        // IPv6：链路本地 fe80::/10、唯一本地地址 fc00::/7
        return (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) || (bytes[0] & 0xfe) == 0xfc;
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        // 极短 key 全量掩码，避免 key[..2] 越界或回显整个 key
        if (key.Length < 4)
        {
            return "****";
        }

        if (key.Length <= 8)
        {
            return key[..2] + "****";
        }

        return key[..4] + "****" + key[^4..];
    }
}
