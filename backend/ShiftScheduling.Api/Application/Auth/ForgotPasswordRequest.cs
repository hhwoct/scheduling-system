namespace ShiftScheduling.Api.Application.Auth;

public sealed record ForgotPasswordRequest(
    string Username,
    string NewPassword,
    string ConfirmPassword,
    string VerifyInfo);