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

    public AuthService(
        ShiftSchedulingDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IPasswordService passwordService,
        ICurrentUser currentUser,
        IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _passwordService = passwordService;
        _currentUser = currentUser;
        _auditLogService = auditLogService;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new BusinessException("用户名和密码不能为空", "INVALID_LOGIN_REQUEST");
        }

        if (username.Length > 50 || request.Password.Length > 128)
        {
            throw new BusinessException("用户名或密码格式不正确", "INVALID_LOGIN_REQUEST");
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username, cancellationToken);

        if (user is null || user.Status != 1 || !_passwordService.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        var currentUser = new CurrentUserResponse(user.Id, user.StoreId, user.Username, user.Nickname, user.Role);
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
            .Select(x => new CurrentUserResponse(x.Id, x.StoreId, x.Username, x.Nickname, x.Role))
            .FirstOrDefaultAsync(cancellationToken);

        return user ?? throw new UnauthorizedBusinessException("当前用户不存在或已被停用");
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.NewPassword) || string.IsNullOrWhiteSpace(request.VerifyInfo))
        {
            throw new BusinessException("用户名、新密码和验证信息不能为空", "INVALID_FORGOT_REQUEST");
        }

        if (username.Length > 50 || request.NewPassword.Length > 128)
        {
            throw new BusinessException("用户名或密码格式不正确", "INVALID_FORGOT_REQUEST");
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

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Username == username && x.Status == 1, cancellationToken)
            ?? throw new InvalidCredentialsException("用户名或验证信息不正确");

        // 验证信息校验（防任意重置）
        if (user.Role == "EMPLOYEE")
        {
            // 员工账号：验证手机号与员工档案一致
            var emp = await _dbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.EmployeeNo == username && x.Status == 1, cancellationToken);
            if (emp is null || emp.Phone != request.VerifyInfo)
            {
                throw new InvalidCredentialsException("用户名或验证信息不正确");
            }
        }
        else
        {
            // 管理员/门店经理：验证手机号属于该门店任何在职员工（防止随意重置）
            var empExists = await _dbContext.Employees
                .AsNoTracking()
                .AnyAsync(x => x.StoreId == user.StoreId && x.Phone == request.VerifyInfo && x.Status == 1, cancellationToken);
            if (!empExists)
            {
                throw new InvalidCredentialsException("用户名或验证信息不正确");
            }
        }

        user.PasswordHash = _passwordService.Hash(request.NewPassword);
        user.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
