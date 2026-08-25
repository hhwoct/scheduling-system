using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        catch (BadHttpRequestException ex)
        {
            // 审查修复（P2）：JSON 解析失败/请求体非法统一按 400 返回，而不是落入通用 500
            await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.BadRequest, "请求格式不正确", "INVALID_REQUEST");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // 审查修复（P2）：并发编辑冲突（乐观锁 Version 令牌）返回 409，前端可提示刷新重试
            _logger.LogWarning(ex, "并发编辑冲突");
            await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.Conflict, "数据已被他人修改，请刷新后重试", "CONCURRENCY_CONFLICT");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "系统异常");
            await ApiResponseWriter.WriteErrorAsync(context, HttpStatusCode.InternalServerError, "系统异常，请稍后重试", "SYSTEM_ERROR");
        }
    }
}
