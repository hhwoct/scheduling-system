namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 发送密码重置验证码请求。
/// </summary>
public sealed record SendResetOtpRequest(string? Username);