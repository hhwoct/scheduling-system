using System.Net;

namespace ShiftScheduling.Api.Application.Common;

public static class ApiResponseWriter
{
    public static Task WriteErrorAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string message,
        string errorCode)
    {
        // 响应已开始发送时无法再修改状态码/写入内容，直接跳过避免二次异常
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, errorCode));
    }
}