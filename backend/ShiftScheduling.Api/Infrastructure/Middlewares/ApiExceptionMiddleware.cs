using System.Net;
using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Infrastructure.Middlewares;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端取消请求：不写入响应，避免无意义错误
        }
        catch (UnauthorizedBusinessException ex)
        {
            await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.Unauthorized, ex.Message, ex.ErrorCode);
        }
        catch (InvalidCredentialsException ex)
        {
            await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.Unauthorized, ex.Message, ex.ErrorCode);
        }
        catch (NotFoundException ex)
        {
            await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.NotFound, ex.Message, ex.ErrorCode);
        }
        catch (BusinessException ex)
        {
            // FORBIDDEN 错误码映射为 403（如审计日志等仅超管可访问的接口），
            // 其余业务错误保持 400，便于客户端区分「无权限」与「参数/业务错误」。
            var status = ex.ErrorCode == "FORBIDDEN" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest;
            await ApiResponseWriter.WriteErrorAsync(context, status, ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "系统异常");
            await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.InternalServerError, "系统异常，请稍后重试", "SYSTEM_ERROR");
        }
    }
}
