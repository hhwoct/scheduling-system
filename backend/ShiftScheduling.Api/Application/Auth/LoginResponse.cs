namespace ShiftScheduling.Api.Application.Auth;

public sealed record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    CurrentUserResponse User);

public sealed record CurrentUserResponse(
    long Id,
    long? StoreId,
    string Username,
    string Nickname,
    string Role);
