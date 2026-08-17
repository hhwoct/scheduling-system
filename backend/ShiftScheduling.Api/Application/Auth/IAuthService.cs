namespace ShiftScheduling.Api.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 发送密码重置验证码（OTP），10 分钟内有效。
    /// </summary>
    Task<string> SendPasswordResetOtpAsync(string username, string? clientIp, CancellationToken cancellationToken);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, string? clientIp, CancellationToken cancellationToken);
}