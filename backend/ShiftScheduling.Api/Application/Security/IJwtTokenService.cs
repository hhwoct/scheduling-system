using ShiftScheduling.Api.Application.Auth;

namespace ShiftScheduling.Api.Application.Security;

public interface IJwtTokenService
{
    JwtTokenResult CreateToken(CurrentUserResponse user);
}

public sealed record JwtTokenResult(string Token, DateTime ExpiresAt);
