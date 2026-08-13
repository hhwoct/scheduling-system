namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 密码重置验证码服务（OTP + 限流）。
/// 使用内存存储，单实例部署适用；多实例部署需替换为 Redis。
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// 生成并存储验证码，返回验证码（开发/测试用）。
    /// </summary>
    string GenerateOtp(string username, string? phone);

    /// <summary>
    /// 验证验证码（单次有效，10 分钟内有效）。
    /// </summary>
    bool ValidateOtp(string username, string? phone, string otp);

    /// <summary>
    /// 检查是否触发限流（按用户名+IP 维度）。
    /// </summary>
    void CheckRateLimit(string username, string? clientIp);

    /// <summary>
    /// 记录一次失败尝试。
    /// </summary>
    void RecordFailure(string username, string? clientIp);

    /// <summary>
    /// 记录一次成功尝试（清除计数）。
    /// </summary>
    void RecordSuccess(string username, string? clientIp);

    /// <summary>
    /// 检查用户是否被锁定。
    /// </summary>
    bool IsLocked(string username, string? clientIp);
}