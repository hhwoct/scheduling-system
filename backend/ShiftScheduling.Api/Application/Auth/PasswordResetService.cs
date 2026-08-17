using System.Collections.Concurrent;
using System.Security.Cryptography;
using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.Auth;

/// <summary>
/// 内存实现：验证码生成/验证 + 失败锁定 + 限流。
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    private const int OtpTtlMinutes = 10;
    private const int MaxAttemptsPerWindow = 5;
    private const int RateLimitWindowMinutes = 5;
    private const int LockoutThreshold = 5;
    private const int LockoutMinutes = 15;

    private sealed record OtpEntry(string Otp, string? Phone, DateTime ExpiresAt, bool Used);

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
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        _otps[key] = new OtpEntry(otp, phone?.Trim(), DateTime.UtcNow.AddMinutes(OtpTtlMinutes), Used: false);
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

        var valid = entry.Otp.Equals(otp.Trim(), StringComparison.Ordinal);
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
        var userKey = BuildKey(username, string.Empty);
        var ipKey = $"ip:{clientIp ?? "unknown"}";
        var now = DateTime.UtcNow;

        // 锁定检查
        foreach (var key in new[] { userKey, ipKey })
        {
            if (_lockouts.TryGetValue(key, out var lockout) && lockout.LockedUntil > now)
            {
                var mins = (int)Math.Ceiling((lockout.LockedUntil - now).TotalMinutes);
                throw new BusinessException($"尝试次数过多，账号已锁定 {mins} 分钟", "ACCOUNT_LOCKED");
            }
        }

        // 限流窗口内尝试计数（用户名 + IP 任一超限即拒绝）
        foreach (var key in new[] { userKey, ipKey })
        {
            var queue = _attempts.GetOrAdd(key, _ => new ConcurrentQueue<AttemptEntry>());
            var cutoff = now.AddMinutes(-RateLimitWindowMinutes);
            while (queue.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                queue.TryDequeue(out _);
            }
            if (queue.Count >= MaxAttemptsPerWindow)
            {
                throw new BusinessException("操作过于频繁，请稍后再试", "RATE_LIMITED");
            }
        }
    }

    public void RecordFailure(string username, string? clientIp)
    {
        var userKey = BuildKey(username, string.Empty);
        var ipKey = $"ip:{clientIp ?? "unknown"}";
        var now = DateTime.UtcNow;

        foreach (var key in new[] { userKey, ipKey })
        {
            var queue = _attempts.GetOrAdd(key, _ => new ConcurrentQueue<AttemptEntry>());
            queue.Enqueue(new AttemptEntry(now));

            // 如果窗口内失败次数达到阈值则锁定
            var cutoff = now.AddMinutes(-RateLimitWindowMinutes);
            while (queue.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                queue.TryDequeue(out _);
            }
            if (queue.Count >= LockoutThreshold)
            {
                _lockouts[key] = new LockoutEntry(now.AddMinutes(LockoutMinutes));
                queue.Clear();
            }
        }
    }

    public void RecordSuccess(string username, string? clientIp)
    {
        var userKey = BuildKey(username, string.Empty);
        _attempts.TryRemove(userKey, out _);
        _lockouts.TryRemove(userKey, out _);

        // 成功登录同时清除对应 IP 的失败计数与锁定，避免误伤共享出口 IP / 残留全站桶
        var ipKey = $"ip:{clientIp ?? "unknown"}";
        _attempts.TryRemove(ipKey, out _);
        _lockouts.TryRemove(ipKey, out _);
    }

    public bool IsLocked(string username, string? clientIp)
    {
        var userKey = BuildKey(username, string.Empty);
        var ipKey = $"ip:{clientIp ?? "unknown"}";
        var now = DateTime.UtcNow;
        return (_lockouts.TryGetValue(userKey, out var l1) && l1.LockedUntil > now)
            || (_lockouts.TryGetValue(ipKey, out var l2) && l2.LockedUntil > now);
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