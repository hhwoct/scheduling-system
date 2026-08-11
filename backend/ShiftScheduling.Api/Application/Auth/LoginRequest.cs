namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 登录请求。使用 class 并重写 ToString，避免密码被日志/异常信息输出。
/// </summary>
public sealed class LoginRequest
{
    public LoginRequest(string? username, string? password)
    {
        Username = username;
        Password = password;
    }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public override string ToString()
        => $"LoginRequest(Username = {Username}, Password = ***)";
}