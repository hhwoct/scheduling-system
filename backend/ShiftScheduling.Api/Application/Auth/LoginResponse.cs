namespace ShiftScheduling.Api.Application.Auth;

public sealed record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    CurrentUserResponse User)
{
    /// <summary>重写 ToString 防止 record 默认打印把令牌泄露进日志。</summary>
    public override string ToString()
        => $"LoginResponse {{ Token = ***, ExpiresAt = {ExpiresAt}, User = {User} }}";
}

public sealed record CurrentUserResponse(
    long Id,
    long? StoreId,
    string Username,
    string Nickname,
    string Role,
    int PasswordVersion);
