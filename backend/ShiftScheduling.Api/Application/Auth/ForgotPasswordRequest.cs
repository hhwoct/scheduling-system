namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 忘记密码请求。使用 class 并重写 ToString，避免密码被日志/异常信息输出。
/// VerifyInfo 为注册手机号：校验「用户名 + 手机号」匹配后直接重置密码（无需验证码）。
/// </summary>
public sealed class ForgotPasswordRequest
{
    public ForgotPasswordRequest(string? username, string? newPassword, string? confirmPassword, string? verifyInfo)
    {
        Username = username;
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
        VerifyInfo = verifyInfo;
    }

    public string? Username { get; init; }

    public string? NewPassword { get; init; }

    public string? ConfirmPassword { get; init; }

    public string? VerifyInfo { get; init; }

    public override string ToString()
        => $"ForgotPasswordRequest(Username = {Username}, VerifyInfo = ***, NewPassword = ***, ConfirmPassword = ***)";
}
