namespace ShiftScheduling.Api.Application.Common;

public sealed record ApiResponse<T>(bool Success, string Message, T? Data, string? ErrorCode = null)
{
    public static ApiResponse<T> Ok(T data, string message = "操作成功") => new(true, message, data);

    public static ApiResponse<T> Fail(string message, string? errorCode = null) => new(false, message, default, errorCode);
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string message = "操作成功") => ApiResponse<T>.Ok(data, message);

    public static ApiResponse<object> Fail(string message, string? errorCode = null) => ApiResponse<object>.Fail(message, errorCode);
}
