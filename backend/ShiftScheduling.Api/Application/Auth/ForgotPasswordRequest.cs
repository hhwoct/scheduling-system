namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 忘记密码请求。使用 class 并重写 ToString，避免密码被日志/异常信息输出。
/// Name 为员工姓名、VerifyInfo 为注册手机号：校验「姓名 + 手机号」匹配后直接重置密码（无需验证码）。
/// </summary>
public sealed class ForgotPasswordRequest
{
    public ForgotPasswordRequest(string? name, string? newPassword, string? confirmPassword, string? verifyInfo)
    {
        Name = name;
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
        VerifyInfo = verifyInfo;
    }

    public string? Name { get; init; }

    public string? NewPassword { get; init; }

    public string? ConfirmPassword { get; init; }

    public string? VerifyInfo { get; init; }

    public override string ToString()
        => $"ForgotPasswordRequest(Name = {Name}, VerifyInfo = ***, NewPassword = ***, ConfirmPassword = ***)";
}
