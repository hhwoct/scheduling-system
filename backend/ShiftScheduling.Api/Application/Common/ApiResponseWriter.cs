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
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, errorCode));
    }
}