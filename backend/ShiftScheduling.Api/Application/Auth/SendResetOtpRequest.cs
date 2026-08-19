namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 发送密码重置验证码请求。
/// VerifyInfo 为注册手机号：验证码与「用户名+手机号」绑定，发送前先校验手机号匹配。
/// </summary>
public sealed record SendResetOtpRequest(string? Username, string? VerifyInfo);