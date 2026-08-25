namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 修改密码请求。使用 class 并重写 ToString，避免密码被日志/异常信息输出。
/// </summary>
public sealed class ChangePasswordRequest
{
    public ChangePasswordRequest(string? oldPassword, string? newPassword, string? confirmPassword)
    {
        OldPassword = oldPassword;
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
    }

    public string? OldPassword { get; init; }

    public string? NewPassword { get; init; }

    public string? ConfirmPassword { get; init; }

    public override string ToString()
        => "ChangePasswordRequest(OldPassword = ***, NewPassword = ***, ConfirmPassword = ***)";
}
