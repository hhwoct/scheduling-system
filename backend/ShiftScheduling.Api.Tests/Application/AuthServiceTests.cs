using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShiftScheduling.Api.Application.Auth;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.Security;
using ShiftScheduling.Api.Infrastructure;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;
using ShiftScheduling.Api.Tests.TestInfra;

namespace ShiftScheduling.Api.Tests.Application;

/// <summary>认证服务单元测试（登录 / 当前用户 / 忘记密码）。</summary>
public sealed class AuthServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly FakeAuditLogService _audit = new();
    private readonly PasswordResetService _passwordReset = new();

    private AuthService CreateService(FakeCurrentUser? currentUser = null)
    {
        var db = _factory.CreateDbContext();
        var jwt = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "ShiftScheduling.Api.Tests",
            Audience = "ShiftScheduling.Admin.Tests",
            SigningKey = "shift-scheduling-tests-signing-key-at-least-32-bytes",
            ExpireMinutes = 30
        }));
        return new AuthService(
            db,
            jwt,
            new BcryptPasswordService(),
            currentUser ?? new FakeCurrentUser(),
            _audit,
            _passwordReset);
    }

    private static UserEntity NewUser(string username = "admin", string role = "STORE_MANAGER", int status = 1, long? storeId = 1)
    {
        var passwordService = new BcryptPasswordService();
        return new UserEntity
        {
            StoreId = storeId,
            Username = username,
            PasswordHash = passwordService.Hash("Passw0rd!"),
            Nickname = "管理员",
            Role = role,
            Status = status,
            PasswordVersion = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static async Task<EmployeeEntity> SeedEmployeeAsync(TestDbContextFactory factory, string no = "E001", string phone = "13800000000")
    {
        var db = factory.CreateDbContext();
        var emp = new EmployeeEntity
        {
            StoreId = 1,
            EmployeeNo = no,
            Name = "员工" + no,
            Phone = phone,
            Department = "楼面",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp;
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokenAndUser()
    {
        var db = _factory.CreateDbContext();
        db.Users.Add(NewUser());
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.LoginAsync(new LoginRequest("admin", "Passw0rd!"), "127.0.0.1", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("admin", result.User.Username);
        Assert.Equal("STORE_MANAGER", result.User.Role);
        Assert.Contains(_audit.Entries, e => e.ActionType == "LOGIN");
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsInvalidCredentials()
    {
        var db = _factory.CreateDbContext();
        db.Users.Add(NewUser());
        await db.SaveChangesAsync();

        var service = CreateService();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest("admin", "wrong-password"), "127.0.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_NonexistentUser_ThrowsInvalidCredentials_NoEnumeration()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest("ghost", "Passw0rd!"), "127.0.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_DisabledUser_ThrowsInvalidCredentials()
    {
        var db = _factory.CreateDbContext();
        db.Users.Add(NewUser(status: 0));
        await db.SaveChangesAsync();

        var service = CreateService();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest("admin", "Passw0rd!"), "127.0.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_EmptyFields_ThrowsBusinessException()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.LoginAsync(new LoginRequest("", ""), "127.0.0.1", CancellationToken.None));
        Assert.Equal("INVALID_LOGIN_REQUEST", ex.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_RepeatedFailures_EventuallyLocked()
    {
        var db = _factory.CreateDbContext();
        db.Users.Add(NewUser());
        await db.SaveChangesAsync();

        var service = CreateService();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
                service.LoginAsync(new LoginRequest("admin", "bad-password"), "127.0.0.1", CancellationToken.None));
        }

        // 第 6 次触发锁定：静默抛 InvalidCredentialsException（锁定不泄露账号状态）
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest("admin", "Passw0rd!"), "127.0.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentUserAsync_NotAuthenticated_ThrowsUnauthorized()
    {
        var service = CreateService(new FakeCurrentUser { IsAuthenticated = false });
        await Assert.ThrowsAsync<UnauthorizedBusinessException>(() =>
            service.GetCurrentUserAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentUserAsync_Authenticated_ReturnsUser()
    {
        var db = _factory.CreateDbContext();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateService(new FakeCurrentUser { IsAuthenticated = true, UserId = user.Id });
        var result = await service.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal("admin", result.Username);
    }

    [Fact]
    public async Task ForgotPasswordAsync_WeakPassword_Throws()
    {
        var db = _factory.CreateDbContext();
        db.Users.Add(NewUser(username: "E001", role: "EMPLOYEE"));
        await db.SaveChangesAsync();
        await SeedEmployeeAsync(_factory);

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ForgotPasswordAsync(
                new ForgotPasswordRequest("E001", "short", "short", "13800000000"),
                "127.0.0.1",
                CancellationToken.None));
        Assert.Equal("WEAK_PASSWORD", ex.ErrorCode);
    }

    [Fact]
    public async Task ForgotPasswordAsync_MismatchedPasswords_Throws()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ForgotPasswordAsync(
                new ForgotPasswordRequest("E001", "NewPassw0rd", "Different0!", "13800000000"),
                "127.0.0.1",
                CancellationToken.None));
        Assert.Equal("PASSWORD_MISMATCH", ex.ErrorCode);
    }

    [Fact]
    public async Task ForgotPasswordAsync_SystemAdminRole_Throws()
    {
        // 忘记密码仅支持 EMPLOYEE/STORE_MANAGER；SYSTEM_ADMIN 账号必须走人工改密
        var db = _factory.CreateDbContext();
        db.Users.Add(NewUser(username: "admin", role: "SYSTEM_ADMIN"));
        await db.SaveChangesAsync();

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.ForgotPasswordAsync(
                new ForgotPasswordRequest("admin", "NewPassw0rd", "NewPassw0rd", "13800000000"),
                "127.0.0.1",
                CancellationToken.None));
        Assert.Equal("INVALID_CREDENTIALS", ex.ErrorCode);
    }

    [Fact]
    public async Task ForgotPasswordAsync_WrongVerifyInfo_Throws()
    {
        var db = _factory.CreateDbContext();
        db.Users.Add(NewUser(username: "E001", role: "EMPLOYEE"));
        await db.SaveChangesAsync();
        await SeedEmployeeAsync(_factory);

        var service = CreateService();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.ForgotPasswordAsync(
                new ForgotPasswordRequest("E001", "NewPassw0rd", "NewPassw0rd", "13999999999"),
                "127.0.0.1",
                CancellationToken.None));
    }

    [Fact]
    public async Task ForgotPasswordAsync_Success_UpdatesHashAndIncrementsPasswordVersion()
    {
        var db = _factory.CreateDbContext();
        var user = NewUser(username: "E001", role: "EMPLOYEE");
        var originalHash = user.PasswordHash;
        var originalVersion = user.PasswordVersion;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await SeedEmployeeAsync(_factory);

        var service = CreateService();
        await service.ForgotPasswordAsync(
            new ForgotPasswordRequest("E001", "NewPassw0rd", "NewPassw0rd", "13800000000"),
            "127.0.0.1",
            CancellationToken.None);

        var reloaded = await db.Users.AsNoTracking().FirstAsync(x => x.Id == user.Id);
        Assert.NotEqual(originalHash, reloaded.PasswordHash);
        Assert.Equal(originalVersion + 1, reloaded.PasswordVersion);
        Assert.Contains(_audit.Entries, e => e.ActionType == "RESET_PASSWORD");
    }
}
