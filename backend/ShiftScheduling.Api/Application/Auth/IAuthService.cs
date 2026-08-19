namespace ShiftScheduling.Api.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 发送密码重置验证码（OTP），10 分钟内有效。
    /// 验证码不返回给调用方（不进入 HTTP 层），仅在服务层用于短信发送/开发日志。
    /// </summary>
    Task SendPasswordResetOtpAsync(SendResetOtpRequest request, string? clientIp, CancellationToken cancellationToken);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, string? clientIp, CancellationToken cancellationToken);
}