namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 修改密码请求。使用 class 并重写 ToString，避免密码被日志/异常信息输出。
/// VerifyInfo 为注册手机号：具备员工档案的账号需与档案手机号一致；无档案账号（admin/manager）忽略。
/// </summary>
public sealed class ChangePasswordRequest
{
    public ChangePasswordRequest(string? oldPassword, string? newPassword, string? confirmPassword, string? verifyInfo)
    {
        OldPassword = oldPassword;
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
        VerifyInfo = verifyInfo;
    }

    public string? OldPassword { get; init; }

    public string? NewPassword { get; init; }

    public string? ConfirmPassword { get; init; }

    public string? VerifyInfo { get; init; }

    public override string ToString()
        => "ChangePasswordRequest(OldPassword = ***, NewPassword = ***, ConfirmPassword = ***, VerifyInfo = ***)";
}
