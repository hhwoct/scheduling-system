using ShiftScheduling.Api.Application.Auth;
using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>验证码/限流/锁定服务单元测试。</summary>
public sealed class PasswordResetServiceTests
{
    [Fact]
    public void GenerateOtp_ReturnsSixDigitCode()
    {
        var service = new PasswordResetService();
        var otp = service.GenerateOtp("alice", "13800000000");

        Assert.Equal(6, otp.Length);
        Assert.True(otp.All(char.IsDigit));
    }

    [Fact]
    public void ValidateOtp_AcceptsCorrectCodeOnce()
    {
        var service = new PasswordResetService();
        var otp = service.GenerateOtp("alice", "13800000000");

        Assert.True(service.ValidateOtp("alice", "13800000000", otp));
        // 单次有效：第二次失败
        Assert.False(service.ValidateOtp("alice", "13800000000", otp));
    }

    [Fact]
    public void ValidateOtp_WrongCodeOrPhone_Fails()
    {
        var service = new PasswordResetService();
        var otp = service.GenerateOtp("alice", "13800000000");

        Assert.False(service.ValidateOtp("alice", "13800000000", "000000"));
        Assert.False(service.ValidateOtp("alice", "13999999999", otp));
        Assert.False(service.ValidateOtp("bob", "13800000000", otp));
    }

    [Fact]
    public void GenerateOtp_WithinCooldown_ThrowsRateLimit()
    {
        var service = new PasswordResetService();
        service.GenerateOtp("alice", "13800000000");

        var ex = Assert.Throws<BusinessException>(() => service.GenerateOtp("alice", "13800000000"));
        Assert.Equal("OTP_RATE_LIMITED", ex.ErrorCode);
    }

    [Fact]
    public void CheckRateLimit_AfterFiveFailures_LocksAccount()
    {
        var service = new PasswordResetService();
        for (var i = 0; i < 5; i++)
        {
            service.RecordFailure("alice", "1.2.3.4");
        }

        Assert.True(service.IsLocked("alice", "1.2.3.4"));
        // 审查同步（60d4c6c 起）：锁定后统一抛 InvalidCredentialsException（静默，不泄露账号状态）
        var ex = Assert.Throws<InvalidCredentialsException>(() => service.CheckRateLimit("alice", "1.2.3.4"));
        Assert.Equal("INVALID_CREDENTIALS", ex.ErrorCode);
    }

    [Fact]
    public void RecordFailure_FiveFailuresInWindow_TriggersLockout()
    {
        var service = new PasswordResetService();
        for (var i = 0; i < 4; i++)
        {
            service.RecordFailure("alice", "1.2.3.4");
            Assert.False(service.IsLocked("alice", "1.2.3.4"));
        }

        // 第 5 次失败触发锁定
        service.RecordFailure("alice", "1.2.3.4");
        Assert.True(service.IsLocked("alice", "1.2.3.4"));

        // 锁定后 CheckRateLimit 静默抛 InvalidCredentialsException（不泄露账号状态）
        var ex = Assert.Throws<InvalidCredentialsException>(() => service.CheckRateLimit("alice", "1.2.3.4"));
        Assert.Equal("INVALID_CREDENTIALS", ex.ErrorCode);
    }

    [Fact]
    public void RecordSuccess_ClearsUserFailureCount()
    {
        var service = new PasswordResetService();
        for (var i = 0; i < 4; i++)
        {
            service.RecordFailure("alice", "1.2.3.4");
        }

        service.RecordSuccess("alice", "1.2.3.4");

        // 用户维度计数已清：换个 IP 再失败 4 次不会触发用户锁定（从 0 开始）
        for (var i = 0; i < 4; i++)
        {
            service.RecordFailure("alice", "5.6.7.8");
        }
        Assert.False(service.IsLocked("alice", "9.9.9.9"));
    }

    [Fact]
    public void IsLocked_DifferentIpStillLockedByUsername()
    {
        var service = new PasswordResetService();
        for (var i = 0; i < 5; i++)
        {
            service.RecordFailure("alice", "1.2.3.4");
        }

        // 用户名维度锁定与 IP 无关
        Assert.True(service.IsLocked("alice", "9.9.9.9"));
    }

    [Fact]
    public void RecordFailure_DifferentUsersDoNotLockEachOther()
    {
        var service = new PasswordResetService();
        for (var i = 0; i < 5; i++)
        {
            service.RecordFailure("alice", "1.2.3.4");
        }

        // 审查同步（60d4c6c 起）：锁定按用户名维度，同 IP 的其他账号不受影响
        Assert.False(service.IsLocked("bob", "1.2.3.4"));

        // 用户名维度隔离：bob 从别的 IP 使用自己的用户名不受影响
        Assert.False(service.IsLocked("bob", "9.9.9.9"));
        service.RecordFailure("bob", "9.9.9.9");
        Assert.False(service.IsLocked("bob", "9.9.9.9"));
    }
}