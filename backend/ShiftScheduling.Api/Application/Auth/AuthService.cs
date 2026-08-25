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

    public AuthService(
        ShiftSchedulingDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IPasswordService passwordService,
        ICurrentUser currentUser,
        IAuditLogService auditLogService,
        IPasswordResetService passwordResetService)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _passwordService = passwordService;
        _currentUser = currentUser;
        _auditLogService = auditLogService;
        _passwordResetService = passwordResetService;
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
            // 失败固定时延：即使客户端回车卡键/连发，也只能约 1 秒/次，
            // 避免瞬间刷满失败次数触发锁定（同时增强防暴力破解）
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            throw new InvalidCredentialsException();
        }

        if (user!.Status != 1)
        {
            // 用户已停用：统一返回"用户名或密码错误"，不泄露账号状态
            _passwordResetService.RecordFailure(username, clientIp);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, string? clientIp, CancellationToken cancellationToken)
    {
        // 新验证口径（用户要求）：姓名 + 手机号 双因素匹配，替代原来的 用户名 + 手机号
        var name = request.Name?.Trim();
        var phone = request.VerifyInfo?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone) ||
            string.IsNullOrWhiteSpace(request.NewPassword) || string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            throw new BusinessException("姓名、手机号、新密码和确认密码不能为空", "INVALID_FORGOT_REQUEST");
        }

        // bcrypt 只取前 72 字节：超过 72 字节会被静默截断，必须在上游拒绝
        if (name.Length > 50 || Encoding.UTF8.GetByteCount(request.NewPassword) > 72)
        {
            throw new BusinessException("姓名或密码格式不正确（密码最长 72 字节）", "INVALID_FORGOT_REQUEST");
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

        // 限流 + 锁定检查（按姓名维度 + IP 限流，防暴力尝试）
        _passwordResetService.CheckRateLimit(name, clientIp);

        // 按「姓名 + 手机号」定位员工（姓名与手机号必须同时匹配，防枚举统一报错）
        var matched = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.Name == name && x.Phone == phone && x.Status == 1)
            .ToListAsync(cancellationToken);
        if (matched.Count != 1)
        {
            _passwordResetService.RecordFailure(name, clientIp);
            throw new InvalidCredentialsException("姓名或验证信息不正确");
        }
        var employee = matched[0];

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Username == employee.EmployeeNo && x.StoreId == employee.StoreId && x.Status == 1, cancellationToken);
        if (user is null)
        {
            _passwordResetService.RecordFailure(name, clientIp);
            throw new InvalidCredentialsException("姓名或验证信息不正确");
        }

        // 角色白名单：仅支持具备员工档案的角色（EMPLOYEE/STORE_MANAGER/SYSTEM_ADMIN，
        // 如 E001 店长）；admin/manager 无员工档案，天然无法通过姓名+手机号匹配
        if (user.Role is not ("EMPLOYEE" or "STORE_MANAGER" or "SYSTEM_ADMIN"))
        {
            _passwordResetService.RecordFailure(name, clientIp);
            throw new InvalidCredentialsException("姓名或验证信息不正确");
        }

        // 审查修复（P2）：与改密口径一致，禁止重置为当前密码
        if (_passwordService.Verify(request.NewPassword, user.PasswordHash))
        {
            throw new BusinessException("新密码不能与当前密码相同", "SAME_PASSWORD");
        }

        // 无验证码：姓名+手机号匹配即视为身份验证通过（限流/锁定由 PasswordResetService 兜底）
        user.PasswordHash = _passwordService.Hash(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        user.PasswordVersion++;  // 使旧 JWT 令牌失效

        await _dbContext.SaveChangesAsync(cancellationToken);

        _passwordResetService.RecordSuccess(name, clientIp);

        await _auditLogService.WriteAsync(
            user.StoreId ?? 1,
            user.Id,
            user.Nickname,
            "RESET_PASSWORD",
            "USER",
            user.Id,
            null,
            $"用户 {user.Username} 通过忘记密码流程（姓名+手机号验证）重置了密码（来源 IP：{clientIp ?? "未知"}）",
            "重置密码",
            cancellationToken);
    }

    /// <summary>
    /// 登录后自助修改密码（安全审查 P1-1/P1-2 加固）：校验旧密码 + 新密码策略 + 手机号，
    /// 成功后 PasswordVersion+1 使全部旧 Token 失效。限流/锁定与失败延时与登录口径一致。
    /// </summary>
    public async Task ChangePasswordAsync(long userId, ChangePasswordRequest request, string? clientIp, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OldPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword) || string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            throw new BusinessException("当前密码、新密码和确认密码不能为空", "INVALID_CHANGE_PASSWORD");
        }

        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new BusinessException("两次输入的新密码不一致", "PASSWORD_MISMATCH");
        }

        // bcrypt 只取前 72 字节：超过必须在上游拒绝
        if (Encoding.UTF8.GetByteCount(request.NewPassword) > 72)
        {
            throw new BusinessException("新密码最长 72 字节", "INVALID_CHANGE_PASSWORD");
        }

        if (request.NewPassword.Length < 8 ||
            !request.NewPassword.Any(char.IsUpper) ||
            !request.NewPassword.Any(char.IsLower) ||
            !request.NewPassword.Any(char.IsDigit))
        {
            throw new BusinessException("新密码至少 8 位，且必须包含大写字母、小写字母和数字", "WEAK_PASSWORD");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new NotFoundException("用户不存在");

        // 审查修复（P1-2）：账号级失败锁定与登录共享，防暴力确认旧密码/枚举手机号。
        // 锁定分支底层抛 InvalidCredentialsException（与登录静默口径一致），改密场景
        // 转为 400 明确提示，避免 401 触发前端拦截器的会话失效自动登出。
        try
        {
            _passwordResetService.CheckRateLimit(user.Username, clientIp);
        }
        catch (InvalidCredentialsException)
        {
            throw new BusinessException("尝试过于频繁，请稍后再试", "RATE_LIMITED");
        }

        if (!_passwordService.Verify(request.OldPassword, user.PasswordHash))
        {
            // 与登录失败同口径：固定时延 + 失败计数（5 次锁定 15 分钟）。
            // 注意：返回 400 而非 401——已认证上下文中的"旧密码错误"是业务校验失败，
            // 若返回 401 会触发前端拦截器的会话失效自动登出，体验异常。
            _passwordResetService.RecordFailure(user.Username, clientIp);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            throw new BusinessException("当前密码不正确", "WRONG_OLD_PASSWORD");
        }

        // 审查修复（P1-2）：手机号校验移到"新旧相同"之前，避免 SAME_PASSWORD 成为旧密码 oracle；
        // 失败同样固定时延。有档案账号必须与档案手机号一致；无档案账号（admin/manager）跳过。
        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == user.Username && x.StoreId == user.StoreId && x.Status == 1, cancellationToken);
        if (employee is not null)
        {
            var verifyPhone = request.VerifyInfo?.Trim();
            if (string.IsNullOrWhiteSpace(employee.Phone))
            {
                // 审查修复（P2-5）：档案未登记手机号 → 无法自助验证，给出明确指引而非笼统失败
                throw new BusinessException("该账号未登记手机号，无法自助修改密码，请联系管理员", "PHONE_NOT_REGISTERED");
            }

            if (string.IsNullOrWhiteSpace(verifyPhone) || employee.Phone != verifyPhone)
            {
                _passwordResetService.RecordFailure(user.Username, clientIp);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                throw new BusinessException("手机号验证失败，请使用注册手机号", "PHONE_MISMATCH");
            }
        }

        if (_passwordService.Verify(request.NewPassword, user.PasswordHash))
        {
            throw new BusinessException("新密码不能与当前密码相同", "SAME_PASSWORD");
        }

        user.PasswordHash = _passwordService.Hash(request.NewPassword);
        user.PasswordVersion++;  // 使旧 JWT 令牌失效，前端改密后引导重新登录
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _passwordResetService.RecordSuccess(user.Username, clientIp);

        await _auditLogService.WriteAsync(
            user.StoreId ?? 1,
            user.Id,
            user.Nickname,
            "CHANGE_PASSWORD",
            "USER",
            user.Id,
            null,
            $"用户 {user.Username} 自助修改了密码（来源 IP：{clientIp ?? "未知"}）",
            "修改密码",
            cancellationToken);
    }
}