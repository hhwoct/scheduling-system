namespace ShiftScheduling.Api.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, string? clientIp, CancellationToken cancellationToken);

    /// <summary>登录后自助修改密码：校验旧密码 + 密码策略，改密后 PasswordVersion+1 使全部旧 Token 失效。</summary>
    Task ChangePasswordAsync(long userId, ChangePasswordRequest request, CancellationToken cancellationToken);
}