using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 内存实现：验证码生成/验证 + 失败锁定 + 限流。
/// 账号锁定机制保留，但锁定后不再提示"账号已锁定"，
/// 统一按"用户名或密码错误"处理（不泄露账号状态）。
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    private const int OtpTtlMinutes = 10;
    private const int MaxAttemptsPerWindow = 5;
    private const int RateLimitWindowMinutes = 5;
    private const int LockoutThreshold = 5;
    private const int LockoutMinutes = 15;
    private const int ResendCooldownSeconds = 60;

    private sealed record OtpEntry(string Otp, string? Phone, DateTime CreatedAt, DateTime ExpiresAt, bool Used);

    private sealed record AttemptEntry(DateTime Timestamp);

    private sealed record LockoutEntry(DateTime LockedUntil);

    private readonly ConcurrentDictionary<string, OtpEntry> _otps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<AttemptEntry>> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LockoutEntry> _lockouts = new(StringComparer.OrdinalIgnoreCase);

    private static string BuildKey(string username, string? phone)
        => $"{username.Trim().ToUpperInvariant()}|{phone?.Trim() ?? string.Empty}";

    public string GenerateOtp(string username, string? phone)
    {
        var key = BuildKey(username, phone);
        var now = DateTime.UtcNow;

        // 重发冷却：60 秒内不允许重复生成，防止短信轰炸与内存占用攻击
        if (_otps.TryGetValue(key, out var existing) && existing.CreatedAt.AddSeconds(ResendCooldownSeconds) > now)
        {
            throw new BusinessException("验证码发送过于频繁，请稍后再试", "OTP_RATE_LIMITED");
        }

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        _otps[key] = new OtpEntry(otp, phone?.Trim(), now, now.AddMinutes(OtpTtlMinutes), Used: false);
        CleanupExpired();
        return otp;
    }

    /// <summary>
    /// 验证验证码（单次有效，10 分钟内有效）。
    /// 使用 TryUpdate 保证并发下的原子性，避免双花。
    /// </summary>
    public bool ValidateOtp(string username, string? phone, string otp)
    {
        if (string.IsNullOrWhiteSpace(otp)) return false;

        var key = BuildKey(username, phone);
        if (!_otps.TryGetValue(key, out var entry)) return false;

        // 时间过期或已验证过
        if (entry.ExpiresAt < DateTime.UtcNow || entry.Used) return false;

        // 常数时间比较，防止时序侧信道枚举验证码
        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(entry.Otp),
            Encoding.ASCII.GetBytes(otp.Trim()));
        if (valid)
        {
            // 原子更新 Used=true；并发时只有一个线程能成功更新
            var updated = _otps.TryUpdate(key, entry with { Used = true }, entry);
            return updated;  // 只有成功更新（未被其他线程先消费）才算有效
        }
        return false;
    }

    public void CheckRateLimit(string username, string? clientIp)
    {
        // 失败计数/锁定按用户名维度（不影响同 IP 的其他账号）。
        // 锁定后不再提示"账号已锁定"，统一返回"用户名或密码错误"（不泄露账号状态）。
        var userKey = BuildKey(username, string.Empty);
        var now = DateTime.UtcNow;

        // 锁定检查（静默：与密码错误表现一致）
        if (_lockouts.TryGetValue(userKey, out var lockout) && lockout.LockedUntil > now)
        {
            throw new InvalidCredentialsException();
        }

        // 限流窗口内尝试计数（仅用户名维度）
        var queue = _attempts.GetOrAdd(userKey, _ => new ConcurrentQueue<AttemptEntry>());
        TrimQueue(queue, now);
        if (queue.Count >= MaxAttemptsPerWindow)
        {
            throw new BusinessException("操作过于频繁，请稍后再试", "RATE_LIMITED");
        }
    }

    public void RecordFailure(string username, string? clientIp)
    {
        // 仅用户名维度记录失败：一个账号密码错多了，不影响同 IP 的其他账号
        var userKey = BuildKey(username, string.Empty);
        var now = DateTime.UtcNow;

        // 锁内完成"检查 + 记录"，消除 CheckRateLimit 与 RecordFailure 之间的竞态窗口
        var queue = _attempts.GetOrAdd(userKey, _ => new ConcurrentQueue<AttemptEntry>());
        lock (queue)
        {
            TrimQueue(queue, now);
            if (queue.Count >= MaxAttemptsPerWindow)
            {
                throw new BusinessException("操作过于频繁，请稍后再试", "RATE_LIMITED");
            }
            queue.Enqueue(new AttemptEntry(now));

            // 窗口内失败次数达到阈值则锁定该账号（锁定期间静默拒绝登录）
            if (queue.Count >= LockoutThreshold)
            {
                _lockouts[userKey] = new LockoutEntry(now.AddMinutes(LockoutMinutes));
                queue.Clear();
            }
        }
    }

    public void RecordSuccess(string username, string? clientIp)
    {
        var userKey = BuildKey(username, string.Empty);
        _attempts.TryRemove(userKey, out _);
        _lockouts.TryRemove(userKey, out _);
    }

    public bool IsLocked(string username, string? clientIp)
    {
        var userKey = BuildKey(username, string.Empty);
        var now = DateTime.UtcNow;
        return _lockouts.TryGetValue(userKey, out var lockout) && lockout.LockedUntil > now;
    }

    private static void TrimQueue(ConcurrentQueue<AttemptEntry> queue, DateTime now)
    {
        var cutoff = now.AddMinutes(-RateLimitWindowMinutes);
        while (queue.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            queue.TryDequeue(out _);
        }
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _otps)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                _otps.TryRemove(kvp.Key, out _);
            }
        }
    }
}