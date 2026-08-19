using Microsoft.Extensions.Configuration;
using ShiftScheduling.Api.Application.Ai;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>AI 配置服务单元测试（URL 白名单/SSRF 防护/Key 掩码）。</summary>
public sealed class AiConfigServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();

    private AiConfigService CreateService(Dictionary<string, string?>? config = null)
    {
        var builder = new ConfigurationBuilder();
        if (config is not null)
        {
            builder.AddInMemoryCollection(config);
        }
        return new AiConfigService(_factory.CreateDbContext(), _audit, builder.Build());
    }

    [Fact]
    public async Task GetAsync_NoConfig_ReturnsUnconfiguredWithDefaults()
    {
        var service = CreateService();
        var item = await service.GetAsync(1, CancellationToken.None);

        Assert.False(item.Configured);
        Assert.Equal("https://api.deepseek.com", item.BaseUrl);
        Assert.Equal("deepseek-chat", item.Model);
        Assert.Equal(string.Empty, item.MaskedKey);
    }

    [Fact]
    public async Task SaveAsync_ValidConfig_StoresAndMasksKey()
    {
        var service = CreateService();
        var item = await service.SaveAsync(
            new AiConfigSaveRequest("sk-test-1234567890", "https://api.deepseek.com", "deepseek-chat"),
            1, 9, "管理员", CancellationToken.None);

        Assert.True(item.Configured);
        Assert.StartsWith("sk-t", item.MaskedKey);
        Assert.DoesNotContain("sk-test-1234567890", item.MaskedKey);
        Assert.Contains(_audit.Entries, e => e.ActionType == "SAVE_AI_CONFIG");
    }

    [Fact]
    public async Task SaveAsync_HttpUrl_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(new AiConfigSaveRequest("sk-x", "http://api.deepseek.com", "deepseek-chat"), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_AI_BASE_URL", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_LocalhostUrl_ThrowsSsrfProtection()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(new AiConfigSaveRequest("sk-x", "https://localhost:8443", "deepseek-chat"), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_AI_BASE_URL", ex.ErrorCode);
    }

    [Theory]
    [InlineData("https://10.0.0.5")]
    [InlineData("https://192.168.1.1")]
    [InlineData("https://169.254.169.254")]   // 云元数据地址
    public async Task SaveAsync_PrivateIpUrl_Throws(string url)
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(new AiConfigSaveRequest("sk-x", url, "deepseek-chat"), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_AI_BASE_URL", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_NonDeepSeekHost_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(new AiConfigSaveRequest("sk-x", "https://evil.example.com", "deepseek-chat"), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_AI_BASE_URL", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_AllowedHostsWhitelist_AllowsConfiguredHost()
    {
        var service = CreateService(new Dictionary<string, string?> { ["Ai:AllowedBaseUrlHosts"] = "proxy.example.com" });
        var item = await service.SaveAsync(
            new AiConfigSaveRequest("sk-x", "https://proxy.example.com", "deepseek-chat"),
            1, 9, "a", CancellationToken.None);

        Assert.Equal("https://proxy.example.com", item.BaseUrl);
    }

    [Fact]
    public async Task SaveAsync_ModelTooLong_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(new AiConfigSaveRequest("sk-x", "https://api.deepseek.com", new string('m', 81)), 1, 9, "a", CancellationToken.None));
        Assert.Equal("INVALID_AI_MODEL", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_BlankApiKey_KeepsExistingKey()
    {
        var service = CreateService();
        await service.SaveAsync(new AiConfigSaveRequest("sk-original-key-123", "https://api.deepseek.com", "deepseek-chat"), 1, 9, "a", CancellationToken.None);

        var updated = await service.SaveAsync(new AiConfigSaveRequest(null, "https://api.deepseek.com", "deepseek-chat"), 1, 9, "a", CancellationToken.None);
        Assert.True(updated.Configured);

        var (apiKey, _, _) = await service.GetEffectiveAsync(1, CancellationToken.None);
        Assert.Equal("sk-original-key-123", apiKey);
    }

    [Fact]
    public async Task GetEffectiveAsync_NotConfigured_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetEffectiveAsync(1, CancellationToken.None));
        Assert.Equal("AI_NOT_CONFIGURED", ex.ErrorCode);
    }
}
