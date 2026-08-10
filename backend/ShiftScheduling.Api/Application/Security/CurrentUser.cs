using System.Security.Claims;

namespace ShiftScheduling.Api.Application.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    long? UserId { get; }

    long? StoreId { get; }

    string? Username { get; }

    string? Nickname { get; }

    string? Role { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public long? UserId => TryGetLong(ClaimTypes.NameIdentifier);

    public long? StoreId => TryGetLong("store_id");

    public string? Username => User?.FindFirst("username")?.Value;

    public string? Nickname => User?.FindFirst("nickname")?.Value;

    public string? Role => User?.FindFirst(ClaimTypes.Role)?.Value;

    private long? TryGetLong(string claimType)
    {
        var value = User?.FindFirst(claimType)?.Value;
        return long.TryParse(value, out var result) ? result : null;
    }
}
