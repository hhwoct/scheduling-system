namespace ShiftScheduling.Api.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);
}
