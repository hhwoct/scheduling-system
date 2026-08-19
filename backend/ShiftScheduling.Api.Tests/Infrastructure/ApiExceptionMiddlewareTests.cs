using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Middlewares;

namespace ShiftScheduling.Api.Tests.Infrastructure;

/// <summary>全局异常中间件单元测试。</summary>
public sealed class ApiExceptionMiddlewareTests
{
    private static async Task<(int Status, string Body)> InvokeAsync(Func<Task> next, bool requestAborted = false)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (requestAborted)
        {
            context.RequestAborted = new CancellationToken(canceled: true);
        }
        var middleware = new ApiExceptionMiddleware(_ => next(), NullLogger<ApiExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    [Fact]
    public async Task BusinessException_MapsTo400WithErrorCode()
    {
        var (status, body) = await InvokeAsync(() =>
            throw new BusinessException("业务错误", "BUSINESS_ERROR"));

        Assert.Equal(400, status);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("业务错误", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("BUSINESS_ERROR", doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task NotFoundException_MapsTo404()
    {
        var (status, _) = await InvokeAsync(() => throw new NotFoundException("数据不存在"));
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task UnauthorizedBusinessException_MapsTo401()
    {
        var (status, _) = await InvokeAsync(() => throw new UnauthorizedBusinessException());
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task InvalidCredentialsException_MapsTo401()
    {
        var (status, _) = await InvokeAsync(() => throw new InvalidCredentialsException());
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task GenericException_MapsTo500WithGenericMessage()
    {
        var (status, body) = await InvokeAsync(() => throw new InvalidOperationException("内部细节"));

        Assert.Equal(500, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("系统异常，请稍后重试", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("SYSTEM_ERROR", doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Cancellation_LeavesResponseUnwritten()
    {
        var (status, body) = await InvokeAsync(() =>
            throw new OperationCanceledException(), requestAborted: true);

        Assert.Equal(200, status); // 默认状态码，未被改写
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public async Task SuccessfulPipeline_NoInterference()
    {
        var (status, body) = await InvokeAsync(async () =>
        {
            await Task.CompletedTask;
            return;
        });
        Assert.Equal(200, status);
    }
}