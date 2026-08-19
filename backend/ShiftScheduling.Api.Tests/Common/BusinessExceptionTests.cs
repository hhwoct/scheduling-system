using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Tests.Common;

/// <summary>业务异常体系单元测试。</summary>
public sealed class BusinessExceptionTests
{
    [Fact]
    public void BusinessException_DefaultErrorCode()
    {
        var ex = new BusinessException("出错了");
        Assert.Equal("BUSINESS_ERROR", ex.ErrorCode);
        Assert.Equal("出错了", ex.Message);
    }

    [Fact]
    public void BusinessException_CustomErrorCode()
    {
        var ex = new BusinessException("冲突", "CONFLICT");
        Assert.Equal("CONFLICT", ex.ErrorCode);
    }

    [Fact]
    public void UnauthorizedBusinessException_HasUnauthorizedCode()
    {
        var ex = new UnauthorizedBusinessException();
        Assert.Equal("UNAUTHORIZED", ex.ErrorCode);
    }

    [Fact]
    public void InvalidCredentialsException_HasInvalidCredentialsCode()
    {
        var ex = new InvalidCredentialsException();
        Assert.Equal("INVALID_CREDENTIALS", ex.ErrorCode);
    }

    [Fact]
    public void NotFoundException_HasNotFoundCode()
    {
        var ex = new NotFoundException("数据不存在");
        Assert.Equal("NOT_FOUND", ex.ErrorCode);
        Assert.Equal("数据不存在", ex.Message);
    }

    [Fact]
    public void Exceptions_AreAssignableToBusinessException()
    {
        Assert.IsAssignableFrom<BusinessException>(new UnauthorizedBusinessException());
        Assert.IsAssignableFrom<BusinessException>(new InvalidCredentialsException());
        Assert.IsAssignableFrom<BusinessException>(new NotFoundException());
    }
}
