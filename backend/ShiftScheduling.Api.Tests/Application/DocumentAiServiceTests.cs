using System.Text.Json;
using ShiftScheduling.Api.Application.Ai;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>AI 文档解析服务单元测试（输入校验 + 模型输出解析，通过桩 HTTP 客户端）。</summary>
public sealed class DocumentAiServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private sealed class FakeAiConfigService : IAiConfigService
    {
        public Task<AiConfigItem> GetAsync(long storeId, CancellationToken cancellationToken)
            => Task.FromResult(new AiConfigItem(true, "DEEPSEEK", "https://api.deepseek.com", "deepseek-chat", "sk-****"));

        public Task<AiConfigItem> SaveAsync(AiConfigSaveRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<(string ApiKey, string BaseUrl, string Model)> GetEffectiveAsync(long storeId, CancellationToken cancellationToken)
            => Task.FromResult(("sk-test", "https://api.deepseek.com", "deepseek-chat"));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }

    private DocumentAiService CreateService(string modelOutput)
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = modelOutput }, finish_reason = "stop" }
            }
        });
        var handler = StubHttpMessageHandler.Ok(body);
        return new DocumentAiService(_factory.CreateDbContext(), new FakeAiConfigService(), new FakeHttpClientFactory(handler));
    }

    [Fact]
    public async Task ParseAsync_SheetRows_ParsesEntries()
    {
        var db = _factory.CreateDbContext();
        db.Workstations.Add(new WorkstationEntity
        {
            StoreId = 1, Code = "FRONT", Name = "前台", SortOrder = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(
            """{"entries":[{"dayType":"WORKDAY","workstation":"前台","timeSlot":"18:00","min":2,"ideal":3}]}""");
        var request = new AiParseRequest(
            "sheet",
            "需求表.xlsx",
            new List<List<string>> { new() { "前台", "18:00", "2" } },
            null,
            null);

        var result = await service.ParseAsync(1, request, CancellationToken.None);

        Assert.Equal(1, result.Parsed);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("WORKDAY", entry.DayType);
        Assert.Equal("18:00", entry.TimeSlot);
        Assert.Equal(2, entry.RequiredCount);
        Assert.Equal(3, entry.IdealCount);
    }

    [Fact]
    public async Task ParseAsync_UnmatchedWorkstation_GoesToSkipped()
    {
        var db = _factory.CreateDbContext();
        db.Workstations.Add(new WorkstationEntity
        {
            StoreId = 1, Code = "FRONT", Name = "前台", SortOrder = 1, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(
            """{"entries":[],"skipped":[{"reason":"第2行：无法识别工作站"}]}""");
        var request = new AiParseRequest("sheet", "a.xlsx", new List<List<string>>(), null, null);

        var result = await service.ParseAsync(1, request, CancellationToken.None);

        Assert.Equal(0, result.Parsed);
        Assert.Equal(1, result.Skipped);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("image/bmp")]
    [InlineData("text/html")]
    public async Task ParseAsync_DisallowedImageMime_Throws(string mime)
    {
        var service = CreateService("{}");
        var request = new AiParseRequest("image", "a.png", null, "aGVsbG8=", mime);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ParseAsync(1, request, CancellationToken.None));
        Assert.Equal("INVALID_AI_IMAGE_MIME", ex.ErrorCode);
    }

    [Fact]
    public async Task ParseAsync_EmptyImage_Throws()
    {
        var service = CreateService("{}");
        var request = new AiParseRequest("image", "a.png", null, "", "image/png");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ParseAsync(1, request, CancellationToken.None));
        Assert.Equal("INVALID_AI_IMAGE", ex.ErrorCode);
    }

    [Fact]
    public async Task ParseAsync_OversizedImage_Throws()
    {
        var service = CreateService("{}");
        var bigBase64 = new string('A', 6 * 1024 * 1024 * 4 / 3 + 1);
        var request = new AiParseRequest("image", "a.png", null, bigBase64, "image/png");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ParseAsync(1, request, CancellationToken.None));
        Assert.Equal("AI_IMAGE_TOO_LARGE", ex.ErrorCode);
    }

    [Fact]
    public async Task ParseAsync_InvalidBase64Characters_Throws()
    {
        var service = CreateService("{}");
        var request = new AiParseRequest("image", "a.png", null, "not base64 !!", "image/png");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ParseAsync(1, request, CancellationToken.None));
        Assert.Equal("INVALID_AI_IMAGE", ex.ErrorCode);
    }

    [Fact]
    public async Task TestAsync_ApiError_ReturnsFailureResult()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"message\":\"invalid api key\"}}")
            });
        var service = new DocumentAiService(
            _factory.CreateDbContext(),
            new FakeAiConfigService(),
            new FakeHttpClientFactory(handler));

        var result = await service.TestAsync(1, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("DeepSeek", result.Message);
    }
}
