using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Tests.Common;

/// <summary>统一响应写入器单元测试。</summary>
public sealed class ApiResponseWriterTests
{
    private static async Task<string> WriteErrorAsync(HttpStatusCode status, string message, string errorCode)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await ApiResponseWriter.WriteErrorAsync(context, status, message, errorCode);

        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
    }

    [Fact]
    public async Task WriteErrorAsync_SetsStatusCodeAndJsonShape()
    {
        var body = await WriteErrorAsync(HttpStatusCode.BadRequest, "参数错误", "INVALID_ARG");

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("参数错误", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("INVALID_ARG", doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task WriteErrorAsync_DifferentStatusCodes_AreSet()
    {
        var body = await WriteErrorAsync(HttpStatusCode.Unauthorized, "未授权", "UNAUTHORIZED");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("UNAUTHORIZED", doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task WriteErrorAsync_ResponseAlreadyStarted_DoesNothing()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Response.StatusCode = 200;
        // 用已开始发送的 ResponseFeature 替换默认实现，模拟"响应已开始"
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(
            new StartedResponseFeature(context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>()!));

        await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.InternalServerError, "x", "Y");

        Assert.Equal(200, context.Response.StatusCode); // 未被改写
    }

    /// <summary>包装 IHttpResponseFeature，强制 HasStarted=true。</summary>
    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        private readonly Microsoft.AspNetCore.Http.Features.IHttpResponseFeature _inner;

        public StartedResponseFeature(Microsoft.AspNetCore.Http.Features.IHttpResponseFeature inner) => _inner = inner;

        public int StatusCode { get => _inner.StatusCode; set => _inner.StatusCode = value; }

        public string? ReasonPhrase { get => _inner.ReasonPhrase; set => _inner.ReasonPhrase = value; }

        public Microsoft.AspNetCore.Http.IHeaderDictionary Headers { get => _inner.Headers; set => _inner.Headers = value; }

        public Stream Body { get => _inner.Body; set => _inner.Body = value; }

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) => _inner.OnStarting(callback, state);

        public void OnCompleted(Func<object, Task> callback, object state) => _inner.OnCompleted(callback, state);
    }
}