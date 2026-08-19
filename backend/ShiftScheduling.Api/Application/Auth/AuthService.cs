using System.Text;
using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.Security;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordService _passwordService;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogService _auditLogService;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILoggerFactory _loggerFactory;

    public AuthService(
        ShiftSchedulingDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IPasswordService passwordService,
        ICurrentUser currentUser,
        IAuditLogService auditLogService,
        IPasswordResetService passwordResetService,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _passwordService = passwordService;
        _currentUser = currentUser;
        _auditLogService = auditLogService;
        _passwordResetService = passwordResetService;
        _environment = environment;
        _loggerFactory = loggerFactory;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new BusinessException("用户名和密码不能为空", "INVALID_LOGIN_REQUEST");
        }

        // bcrypt 只取前 72 字节：超过 72 字节的密码会被静默截断，
        // 两个前 72 字节相同的长密码会互相验证通过，必须在上游拒绝
        if (username.Length > 50 || Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            throw new BusinessException("用户名或密码格式不正确（密码最长 72 字节）", "INVALID_LOGIN_REQUEST");
        }

        // 限流：登录失败次数过多时锁定
        _passwordResetService.CheckRateLimit(username, clientIp);

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username, cancellationToken);

        // 防止用户枚举：无论用户是否存在/状态如何，都执行一次 bcrypt 验证，统一响应时间
        var passwordValid = user is not null
            && _passwordService.Verify(request.Password, user.PasswordHash);

        if (!passwordValid)
        {
            if (user is null)
            {
                // 用户不存在：执行虚拟验证以消耗相同时间（与真实 bcrypt 耗时一致）
                _passwordService.Verify(request.Password, "$2b$12$Ci01D3eY4zXe10SHH5XRCuKKGN2aHGqB1AhsBTBCqQ0tX8xvAmLKO");
            }
            _passwordResetService.RecordFailure(username, clientIp);
            throw new InvalidCredentialsException();
        }

        if (user!.Status != 1)
        {
            // 用户已停用：统一返回"用户名或密码错误"，不泄露账号状态
            _passwordResetService.RecordFailure(username, clientIp);
            throw new InvalidCredentialsException();
        }

        _passwordResetService.RecordSuccess(username, clientIp);

        var currentUser = new CurrentUserResponse(user.Id, user.StoreId, user.Username, user.Nickname, user.Role, user.PasswordVersion);
        var tokenResult = _jwtTokenService.CreateToken(currentUser);

        await _auditLogService.WriteAsync(
            user.StoreId ?? 1,
            user.Id,
            user.Nickname,
            "LOGIN",
            "USER",
            user.Id,
            null,
            $"用户 {user.Username} 登录系统",
            "管理员登录",
            cancellationToken);

        return new LoginResponse(tokenResult.Token, tokenResult.ExpiresAt, currentUser);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            throw new UnauthorizedBusinessException();
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == _currentUser.UserId.Value && x.Status == 1)
            .Select(x => new CurrentUserResponse(x.Id, x.StoreId, x.Username, x.Nickname, x.Role, x.PasswordVersion))
            .FirstOrDefaultAsync(cancellationToken);

        return user ?? throw new UnauthorizedBusinessException("当前用户不存在或已被停用");
    }

    public async Task SendPasswordResetOtpAsync(SendResetOtpRequest request, string? clientIp, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var verifyInfo = request.VerifyInfo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(verifyInfo))
        {
            throw new BusinessException("用户名和注册手机号不能为空", "INVALID_FORGOT_REQUEST");
        }

        // 统一错误消息，不暴露用户是否存在
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.Status == 1, cancellationToken);
        if (user is null || (user.Role != "EMPLOYEE" && user.Role != "STORE_MANAGER"))
        {
            // 为不存在的用户生成一个随机验证码消耗时间，防枚举
            _passwordResetService.CheckRateLimit(username, clientIp);
            await Task.Delay(Random.Shared.Next(300, 800), cancellationToken);
            _passwordResetService.RecordFailure(username, clientIp);
            throw new InvalidCredentialsException("用户名或验证信息不正确");
        }

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == user.StoreId && x.Status == 1, cancellationToken);
        if (employee is null || string.IsNullOrWhiteSpace(employee.Phone) ||
            !string.Equals(employee.Phone, verifyInfo, StringComparison.Ordinal))
        {
            // 手机号与注册信息不符：与用户不存在同等处理，防枚举
            _passwordResetService.RecordFailure(username, clientIp);
            throw new InvalidCredentialsException("用户名或验证信息不正确");
        }

        // 限流 + 锁定检查
        _passwordResetService.CheckRateLimit(username, clientIp);

        var otp = _passwordResetService.GenerateOtp(username, employee.Phone);
        // TODO: 集成短信网关时在此调用发送短信
        // await _smsService.SendAsync(employee.Phone, $"您的排班系统验证码是 {otp}，10 分钟内有效。");

        // 安全：验证码不进入 HTTP 响应。短信网关接入前，仅非生产环境写入服务端日志便于联调。
        if (!_environment.IsProduction())
        {
            _loggerFactory.CreateLogger("PasswordReset").LogWarning(
                "[DEV-OTP] username={Username}, otp={Otp}", username, otp);
        }
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, string? clientIp, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.NewPassword) || string.IsNullOrWhiteSpace(request.VerifyInfo))
        {
            throw new BusinessException("用户名、新密码和验证信息不能为空", "INVALID_FORGOT_REQUEST");
        }

        // bcrypt 只取前 72 字节：超过 72 字节会被静默截断，必须在上游拒绝
        if (username.Length > 50 || Encoding.UTF8.GetByteCount(request.NewPassword) > 72)
        {
            throw new BusinessException("用户名或密码格式不正确（密码最长 72 字节）", "INVALID_FORGOT_REQUEST");
        }

        if (request.NewPassword.Length < 8)
        {
            throw new BusinessException("新密码长度不能少于 8 位", "WEAK_PASSWORD");
        }

        if (!request.NewPassword.Any(char.IsUpper) ||
            !request.NewPassword.Any(char.IsLower) ||
            !request.NewPassword.Any(char.IsDigit))
        {
            throw new BusinessException("新密码必须包含大写字母、小写字母和数字", "WEAK_PASSWORD");
        }

        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new BusinessException("两次输入的密码不一致", "PASSWORD_MISMATCH");
        }

        if (string.IsNullOrWhiteSpace(request.OtpCode))
        {
            throw new BusinessException("验证码不能为空", "INVALID_OTP");
        }

        // 限流 + 锁定检查
        _passwordResetService.CheckRateLimit(username, clientIp);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Username == username && x.Status == 1, cancellationToken)
            ?? throw new InvalidCredentialsException("用户名或验证信息不正确");

        // 验证信息校验（防任意重置 + 防用户枚举）：
        if (user.Role != "EMPLOYEE" && user.Role != "STORE_MANAGER")
        {
            _passwordResetService.RecordFailure(username, clientIp);
            throw new InvalidCredentialsException("用户名或验证信息不正确");
        }

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == user.StoreId && x.Status == 1, cancellationToken);
        if (employee is null || employee.Phone != request.VerifyInfo)
        {
            _passwordResetService.RecordFailure(username, clientIp);
            throw new InvalidCredentialsException("用户名或验证信息不正确");
        }

        // 验证 OTP（单次有效）
        if (!_passwordResetService.ValidateOtp(username, employee.Phone, request.OtpCode))
        {
            _passwordResetService.RecordFailure(username, clientIp);
            throw new BusinessException("验证码错误或已过期", "INVALID_OTP");
        }

        user.PasswordHash = _passwordService.Hash(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        user.PasswordVersion++;  // 使旧 JWT 令牌失效

        await _dbContext.SaveChangesAsync(cancellationToken);

        _passwordResetService.RecordSuccess(username, clientIp);

        await _auditLogService.WriteAsync(
            user.StoreId ?? 1,
            user.Id,
            user.Nickname,
            "RESET_PASSWORD",
            "USER",
            user.Id,
            null,
            $"用户 {user.Username} 通过忘记密码流程重置了密码",
            "重置密码",
            cancellationToken);
    }
}