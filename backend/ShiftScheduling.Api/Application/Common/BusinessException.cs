namespace ShiftScheduling.Api.Application.Common;

public class BusinessException : Exception
{
    public BusinessException(string message, string errorCode = "BUSINESS_ERROR") : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class UnauthorizedBusinessException : BusinessException
{
    public UnauthorizedBusinessException(string message = "未登录或登录已失效") : base(message, "UNAUTHORIZED")
    {
    }
}

public sealed class InvalidCredentialsException : BusinessException
{
    public InvalidCredentialsException(string message = "用户名或密码错误") : base(message, "INVALID_CREDENTIALS")
    {
    }
}

public sealed class NotFoundException : BusinessException
{
    public NotFoundException(string message = "数据不存在") : base(message, "NOT_FOUND")
    {
    }
}
