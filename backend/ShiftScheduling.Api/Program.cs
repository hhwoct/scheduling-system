using System.Net;
using System.Threading;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Application.Ai;
using ShiftScheduling.Api.Application.Auth;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.Employees;
using ShiftScheduling.Api.Application.EmployeeSkills;
using ShiftScheduling.Api.Application.PeakHours;
using ShiftScheduling.Api.Application.Preferences;
using ShiftScheduling.Api.Application.RuleConfigs;
using ShiftScheduling.Api.Application.Schedules;
using ShiftScheduling.Api.Application.Security;
using ShiftScheduling.Api.Application.StaffingRequirements;
using ShiftScheduling.Api.Application.ShiftTemplates;
using ShiftScheduling.Api.Application.Workstations;
using ShiftScheduling.Api.Infrastructure;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Middlewares;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordService, BcryptPasswordService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IEmployeeSkillService, EmployeeSkillService>();
builder.Services.AddScoped<IWorkstationService, WorkstationService>();
builder.Services.AddScoped<IShiftTemplateService, ShiftTemplateService>();
builder.Services.AddScoped<IRuleConfigService, RuleConfigService>();
builder.Services.AddScoped<SchedulingEngine>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IPeakHourService, PeakHourService>();
builder.Services.AddScoped<IStaffingRequirementService, StaffingRequirementService>();
builder.Services.AddScoped<IAiConfigService, AiConfigService>();
builder.Services.AddScoped<IDocumentAiService, DocumentAiService>();
builder.Services.AddScoped<IPreferenceService, PreferenceService>();
builder.Services.AddHttpClient();

var connectionString = builder.Configuration.GetConnectionString("ShiftMvp");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("缺少数据库连接字符串 ConnectionStrings:ShiftMvp");
}

builder.Services.AddDbContext<ShiftSchedulingDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));

// 审计日志使用独立的 DbContext，避免与业务变更共用 ChangeTracker
// 注意：工厂注册为 Scoped（与 AuditLogService 生命周期一致），不能是 Singleton（会与 Scoped 的 DbContextOptions 冲突）
builder.Services.AddDbContextFactory<ShiftSchedulingDbContext>(
    options => options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))),
    ServiceLifetime.Scoped);

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("缺少 JWT 配置");
if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) || string.IsNullOrWhiteSpace(jwtOptions.Audience))
{
    throw new InvalidOperationException("Jwt:Issuer 和 Jwt:Audience 不能为空");
}

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey 至少需要 32 字节，请通过 User Secrets 或环境变量配置");
}

if (jwtOptions.ExpireMinutes is < 5 or > 1440)
{
    throw new InvalidOperationException("Jwt:ExpireMinutes 必须在 5 到 1440 分钟之间");
}

// 超管账号用户名（仅该账号可查看审计日志等敏感数据）：
// 通过配置 SuperAdminUsername 注入，避免硬编码 "admin" 导致超管改名后功能失效。
var superAdminUsername = builder.Configuration["SuperAdminUsername"]?.Trim();
if (string.IsNullOrWhiteSpace(superAdminUsername))
{
    superAdminUsername = "admin";
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!long.TryParse(userIdValue, out var userId))
                {
                    context.Fail("令牌缺少有效用户标识");
                    return;
                }

                var passwordVersionValue = context.Principal?.FindFirst("password_version")?.Value;
                if (!int.TryParse(passwordVersionValue, out var tokenPasswordVersion))
                {
                    context.Fail("令牌缺少密码版本");
                    return;
                }

                var dbContext = context.HttpContext.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
                var user = await dbContext.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == userId && x.Status == 1, context.HttpContext.RequestAborted);

                if (user is null)
                {
                    context.Fail("用户不存在或已被停用");
                    return;
                }

                if (user.PasswordVersion != tokenPasswordVersion)
                {
                    context.Fail("令牌已失效，请重新登录");
                    return;
                }

                // 校验用户所属门店仍处于启用状态：门店停用即吊销访问
                if (user.StoreId is not null)
                {
                    var storeActive = await dbContext.Stores
                        .AsNoTracking()
                        .AnyAsync(x => x.Id == user.StoreId.Value && x.Status == 1, context.HttpContext.RequestAborted);
                    if (!storeActive)
                    {
                        context.Fail("所属门店已停用");
                    }
                }
            },
            OnChallenge = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.HandleResponse();
                await ApiResponseWriter.WriteErrorAsync(
                    context.HttpContext,
                    HttpStatusCode.Unauthorized,
                    "未登录或登录已失效",
                    "UNAUTHORIZED");
            },
            OnForbidden = context => ApiResponseWriter.WriteErrorAsync(
                context.HttpContext,
                HttpStatusCode.Forbidden,
                "没有权限执行此操作",
                "FORBIDDEN")
        };
    });

builder.Services.AddRateLimiter(options =>
{
    // 按客户端 IP 分区限流（防未认证 DoS/洪水）。账号级失败锁定在
    // PasswordResetService 中按"用户名"维度处理（一个账号错密码不影响其他账号），
    // 这里只做洪水防护：登录端点每个 IP 每分钟最多 20 次尝试。
    options.AddPolicy("LoginLimiter", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 密码重置限流：每个 IP 15 分钟最多 3 次尝试
    options.AddPolicy("ResetLimiter", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 被限流时返回统一 JSON 错误结构（前端拦截器直接解析 message 提示）
    options.OnRejected = async (context, cancellationToken) =>
    {
        await ApiResponseWriter.WriteErrorAsync(
            context.HttpContext,
            HttpStatusCode.TooManyRequests,
            "操作过于频繁，请稍后再试",
            "RATE_LIMITED");
    };
});

builder.Services.AddAuthorization(options =>
{
    // 管理端策略：仅系统管理员与门店经理可访问管理接口
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("SYSTEM_ADMIN", "STORE_MANAGER"));

    // 系统管理员专属策略：仅 SYSTEM_ADMIN 可修改关键配置（如排班规则）
    options.AddPolicy("SystemAdminOnly", policy =>
        policy.RequireRole("SYSTEM_ADMIN"));
});
builder.Services.AddOpenApi();

var app = builder.Build();

// 3.15 修复：项目不使用 EF Core Migrations（无迁移文件），数据库结构由
// database/init_shift_mvp.sql 与 database/migrations/ 脚本管理；
// 移除启动时 MigrateAsync，避免无迁移可应用时的空转与模型/DDL 漂移风险。
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/health", () => ApiResponse.Ok(new { status = "UP", service = "ShiftScheduling.Api" }, "后端服务运行正常"));

api.MapPost("/auth/login", async (LoginRequest request, IAuthService authService, HttpContext httpContext, CancellationToken cancellationToken) =>
{
    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
    var result = await authService.LoginAsync(request, clientIp, cancellationToken);
    return ApiResponse.Ok(result, "登录成功");
}).RequireRateLimiting("LoginLimiter");

// 忘记密码：验证用户名 + 注册手机号后直接重置密码（无需验证码）
api.MapPost("/auth/forgot-password", async (
    ForgotPasswordRequest request,
    IAuthService authService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
    await authService.ForgotPasswordAsync(request, clientIp, cancellationToken);
    return ApiResponse.Ok(true, "密码重置成功，请使用新密码登录");
}).RequireRateLimiting("ResetLimiter");

api.MapGet("/auth/me", async (IAuthService authService, CancellationToken cancellationToken) =>
{
    var result = await authService.GetCurrentUserAsync(cancellationToken);
    return ApiResponse.Ok(result, "获取当前用户成功");
}).RequireAuthorization();

api.MapGet("/stores/current", async (ShiftSchedulingDbContext dbContext, ICurrentUser currentUser, CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId
        ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var store = await dbContext.Stores
        .AsNoTracking()
        .Where(x => x.Id == storeId && x.Status == 1)
        .Select(x => new { x.Id, x.Code, x.Name, x.Address, x.MaxEmployeeCount, x.Status })
        .FirstOrDefaultAsync(cancellationToken);

    if (store is null)
    {
        throw new NotFoundException("门店不存在");
    }

    return ApiResponse.Ok(store, "获取当前门店成功");
}).RequireAuthorization();

// ============ 仪表盘统计 ============
api.MapGet("/dashboard/stats", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var employeeCount = await dbContext.Employees.CountAsync(x => x.StoreId == storeId && x.Status == 1, cancellationToken);
    var fullTimeCount = await dbContext.Employees.CountAsync(x => x.StoreId == storeId && x.Status == 1 && x.IsParttime == 0, cancellationToken);
    var partTimeCount = await dbContext.Employees.CountAsync(x => x.StoreId == storeId && x.Status == 1 && x.IsParttime == 1, cancellationToken);
    var shiftCount = await dbContext.ShiftTemplates.CountAsync(x => x.StoreId == storeId && x.Status == 1, cancellationToken);
    var workstationCount = await dbContext.Workstations.CountAsync(x => x.StoreId == storeId && x.Status == 1, cancellationToken);

    return ApiResponse.Ok(new { employeeCount, fullTimeCount, partTimeCount, shiftCount, workstationCount }, "获取统计数据成功");
}).RequireAuthorization("AdminOnly");

// ============ 员工管理 ============
api.MapGet("/employees", async (
    ICurrentUser currentUser,
    IEmployeeService employeeService,
    int page = 1,
    int pageSize = 20,
    string? name = null,
    string? employeeNo = null,
    string? department = null,
    int? status = null,
    CancellationToken cancellationToken = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
    {
        throw new BusinessException("分页参数不正确", "INVALID_PAGINATION");
    }

    var result = await employeeService.QueryAsync(
        new EmployeeQueryRequest(page, pageSize, name, employeeNo, department, status),
        storeId,
        cancellationToken);

    return ApiResponse.Ok(result, "获取员工列表成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/employees/{id:long}", async (
    long id,
    ICurrentUser currentUser,
    IEmployeeService employeeService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await employeeService.GetByIdAsync(id, storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取员工详情成功");
}).RequireAuthorization("AdminOnly");

api.MapPost("/employees", async (
    EmployeeUpsertRequest request,
    ICurrentUser currentUser,
    IEmployeeService employeeService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await employeeService.CreateAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "新增员工成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/employees/{id:long}", async (
    long id,
    EmployeeUpsertRequest request,
    ICurrentUser currentUser,
    IEmployeeService employeeService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await employeeService.UpdateAsync(
        id,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "编辑员工成功");
}).RequireAuthorization("AdminOnly");

api.MapDelete("/employees/{id:long}", async (
    long id,
    ICurrentUser currentUser,
    IEmployeeService employeeService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await employeeService.DeactivateAsync(
        id,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(true, "停用员工成功");
}).RequireAuthorization("AdminOnly");

// ============ 员工技能 ============
api.MapGet("/employees/{employeeId:long}/skills", async (
    long employeeId,
    ICurrentUser currentUser,
    IEmployeeSkillService employeeSkillService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await employeeSkillService.GetByEmployeeAsync(employeeId, storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取员工技能成功");
}).RequireAuthorization("AdminOnly");

// 门店技能等级总览（员工 × 工作站矩阵）
api.MapGet("/skill-matrix", async (
    ICurrentUser currentUser,
    IEmployeeSkillService employeeSkillService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await employeeSkillService.GetStoreMatrixAsync(storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取技能等级总览成功");
}).RequireAuthorization("AdminOnly");

// 单格技能修改（技能等级总览页；管理员与店长均可操作）
api.MapPut("/skill-matrix/cell", async (
    SkillMatrixCellUpdateRequest request,
    ICurrentUser currentUser,
    IEmployeeSkillService employeeSkillService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await employeeSkillService.UpdateCellAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);
    return ApiResponse.Ok(result, "修改技能成功");
}).RequireAuthorization("AdminOnly");

// 员工通岗设置（管理员/店长）
api.MapPut("/skill-matrix/generalist", async (
    SkillMatrixGeneralistRequest request,
    ICurrentUser currentUser,
    IEmployeeSkillService employeeSkillService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await employeeSkillService.SetGeneralistAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);
    return ApiResponse.Ok(result, result.IsGeneralist == 1 ? "已设置通岗" : "已取消通岗");
}).RequireAuthorization("AdminOnly");

api.MapPut("/employees/{employeeId:long}/skills", async (
    long employeeId,
    EmployeeSkillSaveRequest request,
    ICurrentUser currentUser,
    IEmployeeSkillService employeeSkillService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await employeeSkillService.SaveAsync(
        employeeId,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(true, "保存员工技能成功");
}).RequireAuthorization("AdminOnly");

// ============ 工作站 ============
api.MapGet("/workstations", async (
    ICurrentUser currentUser,
    IWorkstationService workstationService,
    bool includeInactive = false,
    CancellationToken cancellationToken = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await workstationService.ListAllAsync(storeId, includeInactive, cancellationToken);
    return ApiResponse.Ok(result, "获取工作站列表成功");
}).RequireAuthorization("AdminOnly");

api.MapPost("/workstations", async (
    WorkstationCreateRequest request,
    ICurrentUser currentUser,
    IWorkstationService workstationService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await workstationService.CreateAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "新增工作站成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/workstations/{id:long}", async (
    long id,
    WorkstationUpdateRequest request,
    ICurrentUser currentUser,
    IWorkstationService workstationService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await workstationService.UpdateAsync(
        id,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "编辑工作站成功");
}).RequireAuthorization("AdminOnly");

// ============ 班次模板 ============
api.MapGet("/shift-templates", async (
    ICurrentUser currentUser,
    IShiftTemplateService shiftTemplateService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await shiftTemplateService.ListAllAsync(storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取班次列表成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/shift-templates/{id:long}", async (
    long id,
    ShiftTemplateUpdateRequest request,
    ICurrentUser currentUser,
    IShiftTemplateService shiftTemplateService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await shiftTemplateService.UpdateAsync(
        id,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "编辑班次成功");
}).RequireAuthorization("AdminOnly");

// ============ 规则配置 ============
api.MapGet("/rules", async (
    ICurrentUser currentUser,
    IRuleConfigService ruleConfigService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await ruleConfigService.ListAllAsync(storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取规则配置成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/rules/{id:long}", async (
    long id,
    RuleConfigUpdateRequest request,
    ICurrentUser currentUser,
    IRuleConfigService ruleConfigService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await ruleConfigService.UpdateAsync(
        id,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "保存规则配置成功");
}).RequireAuthorization("SystemAdminOnly");

// ============ 高峰禁休时段（班中休息禁止与高峰重叠，admin 端增删改查） ============
api.MapGet("/peak-restricted-hours", async (
    ICurrentUser currentUser,
    IPeakHourService peakHourService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await peakHourService.ListAsync(storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取高峰时段成功");
}).RequireAuthorization("AdminOnly");

api.MapPost("/peak-restricted-hours", async (
    PeakHourUpsertRequest request,
    ICurrentUser currentUser,
    IPeakHourService peakHourService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await peakHourService.CreateAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "新增高峰时段成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/peak-restricted-hours/{id:long}", async (
    long id,
    PeakHourUpsertRequest request,
    ICurrentUser currentUser,
    IPeakHourService peakHourService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await peakHourService.UpdateAsync(
        id,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "修改高峰时段成功");
}).RequireAuthorization("AdminOnly");

api.MapDelete("/peak-restricted-hours/{id:long}", async (
    long id,
    ICurrentUser currentUser,
    IPeakHourService peakHourService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await peakHourService.DeleteAsync(
        id,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(true, "删除高峰时段成功");
}).RequireAuthorization("AdminOnly");

// ============ 人数需求配置 ============
api.MapGet("/staffing-requirements", async (
    string? dayType,
    ICurrentUser currentUser,
    IStaffingRequirementService staffingRequirementService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await staffingRequirementService.ListAsync(storeId, dayType, cancellationToken);
    return ApiResponse.Ok(result, "获取人数需求成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/staffing-requirements", async (
    StaffingRequirementSaveRequest request,
    ICurrentUser currentUser,
    IStaffingRequirementService staffingRequirementService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await staffingRequirementService.SaveAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "保存人数需求成功");
}).RequireAuthorization("AdminOnly");

// ============ 人数需求预览（按周期统计） ============
api.MapGet("/staffing-requirements/preview", async (
    DateOnly startDate,
    DateOnly endDate,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    if (startDate > endDate || endDate.DayNumber - startDate.DayNumber > 31)
    {
        throw new BusinessException("日期范围无效（开始不能晚于结束，且周期不超过 31 天）", "INVALID_DATE_RANGE");
    }

    // 需要前一天的日期类型做凌晨归属，从 startDate-1 开始取
    var dateTypes = await dbContext.DateParameters.AsNoTracking()
        .Where(x => x.StoreId == storeId && x.WorkDate >= startDate.AddDays(-1) && x.WorkDate <= endDate)
        .ToDictionaryAsync(x => x.WorkDate, x => x.DayType, cancellationToken);

    var reqs = await dbContext.StaffingRequirements.AsNoTracking()
        .Where(x => x.StoreId == storeId)
        .Select(x => new { x.DayType, x.WorkstationId, x.TimeSlot, x.RequiredCount, x.IdealCount })
        .ToListAsync(cancellationToken);

    var aggregates = new Dictionary<string, (int Days, decimal MinHours, decimal IdealHours, int PeakMin, int PeakIdeal)>
    {
        ["WORKDAY"] = (0, 0m, 0m, 0, 0),
        ["WEEKEND"] = (0, 0m, 0m, 0, 0),
        ["HOLIDAY"] = (0, 0m, 0m, 0, 0)
    };

    foreach (var date in Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
                 .Select(i => startDate.AddDays(i)))
    {
        var dayType = dateTypes.GetValueOrDefault(date) ?? "WORKDAY";
        var prevType = dateTypes.GetValueOrDefault(date.AddDays(-1)) ?? dayType;

        // 营业日口径：<06:00 的凌晨时段归前一天类型；只有行的 day_type 与归属类型一致才计入
        var slotAgg = new Dictionary<(string Type, TimeSpan Slot), (int Min, int Ideal)>();
        foreach (var r in reqs)
        {
            var type = r.TimeSlot < TimeSpan.FromHours(6) ? prevType : dayType;
            if (r.DayType != type)
            {
                continue;
            }

            var key = (type, r.TimeSlot);
            slotAgg.TryGetValue(key, out var acc);
            slotAgg[key] = (acc.Min + r.RequiredCount, acc.Ideal + (r.IdealCount > 0 ? r.IdealCount : r.RequiredCount));
        }

        foreach (var group in slotAgg.GroupBy(x => x.Key.Type))
        {
            // 3.12 修复：数据库中出现未支持的日期类型时跳过统计，而非 KeyNotFound → 500
            if (!aggregates.TryGetValue(group.Key, out var acc))
            {
                continue;
            }

            aggregates[group.Key] = (
                acc.Days,
                acc.MinHours + group.Sum(x => x.Value.Min) * 0.5m,
                acc.IdealHours + group.Sum(x => x.Value.Ideal) * 0.5m,
                Math.Max(acc.PeakMin, group.Max(x => x.Value.Min)),
                Math.Max(acc.PeakIdeal, group.Max(x => x.Value.Ideal)));
        }

        // 各类型营业日天数统计
        foreach (var t in StaffingDayTypes.All)
        {
            if (dayType == t)
            {
                var acc = aggregates[t];
                aggregates[t] = (acc.Days + 1, acc.MinHours, acc.IdealHours, acc.PeakMin, acc.PeakIdeal);
            }
        }
    }

    var byType = aggregates.ToDictionary(
        kv => kv.Key,
        kv => new
        {
            Days = kv.Value.Days,
            MinHours = Math.Round(kv.Value.MinHours, 1),
            IdealHours = Math.Round(kv.Value.IdealHours, 1),
            PeakMin = kv.Value.PeakMin,
            PeakIdeal = kv.Value.PeakIdeal
        });

    return ApiResponse.Ok(new
    {
        ByType = byType,
        TotalMinHours = Math.Round(aggregates.Values.Sum(x => x.MinHours), 1),
        TotalIdealHours = Math.Round(aggregates.Values.Sum(x => x.IdealHours), 1)
    }, "获取人数需求预览成功");
}).RequireAuthorization("AdminOnly");

// ============ AI 文档识别 ============
api.MapGet("/ai/config", async (
    ICurrentUser currentUser,
    IAiConfigService aiConfigService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await aiConfigService.GetAsync(storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取 AI 配置成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/ai/config", async (
    AiConfigSaveRequest request,
    ICurrentUser currentUser,
    IAiConfigService aiConfigService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await aiConfigService.SaveAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);
    return ApiResponse.Ok(result, "保存 AI 配置成功");
}).RequireAuthorization("AdminOnly");

api.MapPost("/ai/test", async (
    ICurrentUser currentUser,
    IDocumentAiService documentAiService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await documentAiService.TestAsync(storeId, cancellationToken);
    return result.Success
        ? ApiResponse.Ok(result, result.Message)
        : ApiResponse.Ok(result, "AI 连通性测试未通过");
}).RequireAuthorization("AdminOnly");

api.MapPost("/ai/parse-requirement-doc", async (
    AiParseRequest request,
    ICurrentUser currentUser,
    IDocumentAiService documentAiService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await documentAiService.ParseAsync(storeId, request, cancellationToken);
    return ApiResponse.Ok(result, "AI 识别完成");
}).RequireAuthorization("AdminOnly");

// ============ 排班业务 ============
api.MapPost("/schedules/generate", async (
    GenerateScheduleRequest request,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await scheduleService.GenerateAsync(
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "一键生成排班成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/schedules", async (
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    int page = 1,
    int pageSize = 20,
    string? status = null,
    CancellationToken cancellationToken = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
    {
        throw new BusinessException("分页参数不正确", "INVALID_PAGINATION");
    }

    var result = await scheduleService.ListPlansAsync(page, pageSize, storeId, status, cancellationToken);
    return ApiResponse.Ok(result, "获取排班计划列表成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/schedules/{planId:long}/month-view", async (
    long planId,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await scheduleService.GetMonthViewAsync(planId, storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取月视图成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/schedules/{planId:long}/week-view", async (
    long planId,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    DateOnly? weekStart = null,
    CancellationToken cancellationToken = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await scheduleService.GetWeekViewAsync(planId, storeId, weekStart, cancellationToken);
    return ApiResponse.Ok(result, "获取周视图成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/schedules/{planId:long}/daily-view", async (
    long planId,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    DateOnly? workDate = null,
    CancellationToken cancellationToken = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    if (workDate is null)
    {
        throw new BusinessException("请指定查询日期 workDate", "INVALID_DATE");
    }

    var result = await scheduleService.GetDailyViewAsync(planId, storeId, workDate.Value, cancellationToken);
    return ApiResponse.Ok(result, "获取日明细成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/schedules/{planId:long}/summary", async (
    long planId,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await scheduleService.GetSummaryAsync(planId, storeId, cancellationToken);
    return ApiResponse.Ok(result, "获取排班摘要成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/schedules/{planId:long}/adjust", async (
    long planId,
    AdjustScheduleRequest request,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await scheduleService.AdjustAsync(
        planId,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(true, "手动调整排班成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/schedules/{planId:long}/day-status", async (
    long planId,
    SetDayStatusRequest request,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await scheduleService.SetDayStatusAsync(
        planId,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(true, "设置员工休息/上班状态成功");
}).RequireAuthorization("AdminOnly");

// 拖动移动工作段（时间平移 + 换工作站）
api.MapPut("/schedules/{planId:long}/move-segment", async (
    long planId,
    MoveScheduleSegmentRequest request,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await scheduleService.MoveSegmentAsync(
        planId,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);
    return ApiResponse.Ok(result, "移动成功");
}).RequireAuthorization("AdminOnly");

api.MapPut("/schedules/{planId:long}/slot-status", async (
    long planId,
    SetSlotStatusRequest request,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await scheduleService.SetSlotStatusAsync(
        planId,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(true, "调整时段状态成功");
}).RequireAuthorization("AdminOnly");

api.MapPost("/schedules/{planId:long}/publish", async (
    long planId,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    bool force,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await scheduleService.PublishAsync(
        planId,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        force,
        cancellationToken);

    // 3.9 修复：员工发布通知已在 ScheduleService.PublishAsync 事务内生成，
    // 此处不再重复发送（原实现位于事务外，失败会 500 但排班已发布）
    return ApiResponse.Ok(true, "排班发布成功");
}).RequireAuthorization("AdminOnly");

// 复制上周（P2）：以最近一期已发布排班为起点生成草稿
api.MapPost("/schedules/{planId:long}/copy-previous", async (
    long planId,
    IScheduleService scheduleService,
    ICurrentUser currentUser,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await scheduleService.CopyPreviousAsync(
        planId,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        ct);
    return ApiResponse.Ok(true, "已从上周排班复制（按星期几对齐，可在此基础上微调）");
}).RequireAuthorization("AdminOnly");

// 发布前调整摘要：对比生成快照（generated_summary_snapshot）与当前日汇总，
// 返回「员工×日期」维度的调整分类统计（改休 / 换班），供发布确认框展示（P0 交互）。
api.MapGet("/schedules/{planId:long}/adjustment-summary", async (
    long planId,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var plan = await db.SchedulePlans.AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == planId && x.StoreId == storeId, ct)
        ?? throw new NotFoundException("排班计划不存在");

    var restChanges = 0;
    var shiftChanges = 0;
    var hasSnapshot = !string.IsNullOrWhiteSpace(plan.GeneratedSummarySnapshot);

    if (hasSnapshot)
    {
        List<SummarySnapshotRow>? snapshot = null;
        try
        {
            snapshot = System.Text.Json.JsonSerializer.Deserialize<List<SummarySnapshotRow>>(plan.GeneratedSummarySnapshot!);
        }
        catch
        {
            snapshot = null;
        }

        if (snapshot is not null && snapshot.Count > 0)
        {
            var snapshotMap = snapshot
                .GroupBy(s => (s.EmployeeId, s.WorkDate))
                .ToDictionary(g => g.Key, g => g.First());

            var finalRows = await db.ScheduleSummaries.AsNoTracking()
                .Where(x => x.PlanId == planId && x.StoreId == storeId)
                .Select(x => new { x.EmployeeId, x.WorkDate, x.IsRestDay, x.ShiftTemplateId })
                .ToListAsync(ct);

            foreach (var f in finalRows)
            {
                if (!snapshotMap.TryGetValue((f.EmployeeId, f.WorkDate), out var s))
                {
                    continue;
                }

                if ((f.IsRestDay == 1) != (s.IsRestDay == 1))
                {
                    restChanges++;
                }
                else if (f.IsRestDay == 0 && (f.ShiftTemplateId ?? 0) != (s.ShiftId ?? 0))
                {
                    shiftChanges++;
                }
            }
        }
    }

    return ApiResponse.Ok(new
    {
        planId,
        planName = plan.PlanName,
        totalAdjustments = restChanges + shiftChanges,
        restChanges,
        shiftChanges,
        hasSnapshot
    }, "获取调整摘要成功");
}).RequireAuthorization("AdminOnly");

// 调整明细列表（店长修改全程记录，按计划查询，倒序分页）
api.MapGet("/schedules/{planId:long}/adjustments", async (
    long planId,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    int page = 1,
    int pageSize = 50,
    CancellationToken ct = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    if (page < 1 || pageSize is < 1 or > 200)
    {
        throw new BusinessException("分页参数不正确", "INVALID_PAGINATION");
    }

    var planExists = await db.SchedulePlans.AsNoTracking()
        .AnyAsync(x => x.Id == planId && x.StoreId == storeId, ct);
    if (!planExists)
    {
        throw new NotFoundException("排班计划不存在");
    }

    var query = db.ScheduleAdjustments.AsNoTracking()
        .Where(x => x.PlanId == planId && x.StoreId == storeId);

    var total = await query.CountAsync(ct);
    var items = await query
        .OrderByDescending(x => x.CreatedAt)
        .ThenByDescending(x => x.Id)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Join(
            db.Employees.AsNoTracking(),
            a => a.EmployeeId,
            e => (long?)e.Id,
            (a, e) => new
            {
                a.Id,
                a.ActionType,
                a.WorkDate,
                a.TimeSlot,
                a.EmployeeId,
                EmployeeNo = e.EmployeeNo,
                EmployeeName = e.Name,
                a.BeforeJson,
                a.AfterJson,
                a.OperatorName,
                a.CreatedAt
            })
        .ToListAsync(ct);

    return ApiResponse.Ok(PagedResult<object>.Create(page, pageSize, total, items), "获取调整明细成功");
}).RequireAuthorization("AdminOnly");

// 需求联动建议：店长反复手动补人的时段 → 提示调整人数需求（P1 交互）
api.MapGet("/schedules/{planId:long}/demand-insights", async (
    long planId,
    IPreferenceService preferenceService,
    ICurrentUser currentUser,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await preferenceService.GetDemandInsightsAsync(planId, storeId, ct);
    return ApiResponse.Ok(result, "获取需求联动建议成功");
}).RequireAuthorization("AdminOnly");

// ============ 偏好学习（feature/schedule-pref-learning） ============
api.MapGet("/preferences/stats", async (
    IPreferenceService preferenceService,
    ICurrentUser currentUser,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await preferenceService.GetStatsAsync(storeId, ct);
    return ApiResponse.Ok(result, "获取偏好学习统计成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/preferences/matrix", async (
    IPreferenceService preferenceService,
    ICurrentUser currentUser,
    string? dayType = null,
    CancellationToken ct = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await preferenceService.GetMatrixAsync(storeId, dayType, ct);
    return ApiResponse.Ok(result, "获取偏好矩阵成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/preferences/top", async (
    IPreferenceService preferenceService,
    ICurrentUser currentUser,
    int limit = 20,
    CancellationToken ct = default) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await preferenceService.GetTopAsync(storeId, limit, ct);
    return ApiResponse.Ok(result, "获取偏好排行成功");
}).RequireAuthorization("AdminOnly");

api.MapGet("/preferences/trends", async (
    IPreferenceService preferenceService,
    ICurrentUser currentUser,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await preferenceService.GetTrendsAsync(storeId, ct);
    return ApiResponse.Ok(result, "获取偏好趋势成功");
}).RequireAuthorization("AdminOnly");

// 手动触发学习重建（调试/导入历史样本用；发布排班时也会自动重建）
api.MapPost("/preferences/rebuild", async (
    IPreferenceService preferenceService,
    ICurrentUser currentUser,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await preferenceService.RebuildAsync(storeId, ct);
    return ApiResponse.Ok(result, "偏好学习重建成功");
}).RequireAuthorization("AdminOnly");

// ============ 站内通知 ============
// 获取通知列表（管理端按门店，员工端按员工 ID；分页，每页 20 条）
api.MapGet("/notifications", async (
    HttpContext httpCtx,
    int page = 1,
    int pageSize = 20,
    string? employeeNo = null) =>
{
    var db = httpCtx.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
    var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    IQueryable<NotificationEntity> query = db.Notifications.AsNoTracking().Where(x => x.StoreId == storeId);

    // 员工只看自己的；管理端可预览指定员工
    var isEmployee = currentUser.Role == "EMPLOYEE";
    var lookupNo = isEmployee ? currentUser.Username : (string.IsNullOrWhiteSpace(employeeNo) ? null : employeeNo.Trim());

    if (lookupNo != null)
    {
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == lookupNo && x.StoreId == storeId && x.Status == 1);
        if (emp != null)
            query = query.Where(x => x.ReceiverEmployeeId == emp.Id);
        else
        {
            httpCtx.Response.StatusCode = 200;
            await httpCtx.Response.WriteAsJsonAsync(ApiResponse.Ok(System.Array.Empty<object>(), "获取通知列表成功"));
            return;
        }
    }
    else
    {
        // 管理端未指定员工：显示门店级通知（新请假/新换班等提醒）+ 发给当前管理员本人的通知
        // （如「排班已发布」），不显示发给员工的个人通知（如「您的请假已批准」）
        query = query.Where(x => x.ReceiverEmployeeId == null && (x.ReceiverUserId == null || x.ReceiverUserId == currentUser.UserId));
    }

    // 分页：每页 pageSize（默认 20）条，返回总数供前端翻页
    if (page < 1) page = 1;
    if (pageSize is < 1 or > 100) pageSize = 20;

    var total = await query.CountAsync();
    var items = await query.OrderByDescending(x => x.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new { x.Id, x.NotificationType, x.Title, x.Content, x.IsRead, x.CreatedAt })
        .ToListAsync();

    httpCtx.Response.StatusCode = 200;
    await httpCtx.Response.WriteAsJsonAsync(ApiResponse.Ok(new { items, total, page, pageSize }, "获取通知列表成功"));
}).RequireAuthorization();

// 获取未读数量
api.MapGet("/notifications/unread-count", async (
    HttpContext httpCtx,
    string? employeeNo = null) =>
{
    var db = httpCtx.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
    var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    IQueryable<NotificationEntity> query = db.Notifications.AsNoTracking().Where(x => x.StoreId == storeId && x.IsRead == 0);

    var isEmployee = currentUser.Role == "EMPLOYEE";
    var lookupNo = isEmployee ? currentUser.Username : (string.IsNullOrWhiteSpace(employeeNo) ? null : employeeNo.Trim());

    if (lookupNo != null)
    {
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == lookupNo && x.StoreId == storeId && x.Status == 1);
        if (emp != null)
            query = query.Where(x => x.ReceiverEmployeeId == emp.Id);
        else
        {
            httpCtx.Response.StatusCode = 200;
            await httpCtx.Response.WriteAsJsonAsync(ApiResponse.Ok(new { Count = 0 }, "获取未读数量成功"));
            return;
        }
    }
    else
    {
        // 管理端未指定员工：统计门店级未读通知 + 发给当前管理员本人的未读通知
        query = query.Where(x => x.ReceiverEmployeeId == null && (x.ReceiverUserId == null || x.ReceiverUserId == currentUser.UserId));
    }

    var count = await query.CountAsync();
    httpCtx.Response.StatusCode = 200;
    await httpCtx.Response.WriteAsJsonAsync(ApiResponse.Ok(new { Count = count }, "获取未读数量成功"));
}).RequireAuthorization();

// 标记已读
api.MapPut("/notifications/{id:long}/read", async (
    long id,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    // 员工只能标记本人通知；店长可标记本人 + 门店级通知；系统管理员可标记门店内任意通知
    var query = db.Notifications.Where(x => x.Id == id && x.StoreId == storeId);
    if (currentUser.Role == "EMPLOYEE")
    {
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == currentUser.Username && x.StoreId == storeId && x.Status == 1, ct)
            ?? throw new NotFoundException("员工档案不存在");
        query = query.Where(x => x.ReceiverEmployeeId == emp.Id);
    }
    else if (currentUser.Role == "STORE_MANAGER")
    {
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == currentUser.Username && x.StoreId == storeId && x.Status == 1, ct)
            ?? throw new NotFoundException("员工档案不存在");
        query = query.Where(x => x.ReceiverEmployeeId == emp.Id || x.ReceiverEmployeeId == null);
    }

    var notification = await query.FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("通知不存在");

    notification.IsRead = 1;
    notification.ReadAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    return ApiResponse.Ok(true, "已标记为已读");
}).RequireAuthorization();

// 全部标记已读
api.MapPut("/notifications/read-all", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    IQueryable<NotificationEntity> query = db.Notifications.Where(x => x.StoreId == storeId && x.IsRead == 0);

    if (currentUser.Role == "EMPLOYEE")
    {
        // 2.3 修复：档案缺失/停用必须终止，否则 query 保持全店范围会清空整店未读
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == currentUser.Username && x.StoreId == storeId && x.Status == 1, ct)
            ?? throw new NotFoundException("员工档案不存在");
        query = query.Where(x => x.ReceiverEmployeeId == emp.Id);
    }
    else
    {
        // 管理端默认列表只展示门店级通知，全部已读也只作用于门店级通知，
        // 避免连带清掉员工个人通知的未读状态
        query = query.Where(x => x.ReceiverEmployeeId == null && x.ReceiverUserId == null);
    }

    var now = DateTime.UtcNow;
    await query.ExecuteUpdateAsync(x => x.SetProperty(n => n.IsRead, 1).SetProperty(n => n.ReadAt, now), ct);
    return ApiResponse.Ok(true, "已全部标记为已读");
}).RequireAuthorization();

// ============ 审计日志 ============
// 仅超管账号可查看（E001 等 SYSTEM_ADMIN 角色也不放行；用户名由 SuperAdminUsername 配置）
api.MapGet("/audit-logs", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    int page = 1,
    int pageSize = 20,
    string? actionType = null,
    string? startDate = null,
    string? endDate = null,
    CancellationToken cancellationToken = default) =>
{
    // 审计日志仅超管账号可查看（即使 SYSTEM_ADMIN 角色的其他账号也无权限）
    if (currentUser.Username != superAdminUsername)
    {
        throw new BusinessException("没有权限执行此操作", "FORBIDDEN");
    }

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
    {
        throw new BusinessException("分页参数不正确", "INVALID_PAGINATION");
    }

    var query = dbContext.AuditLogs
        .AsNoTracking()
        .Where(x => x.StoreId == storeId);

    if (!string.IsNullOrWhiteSpace(actionType))
    {
        query = query.Where(x => x.ActionType == actionType);
    }

    if (!string.IsNullOrWhiteSpace(startDate) && DateOnly.TryParse(startDate, out var start))
    {
        var startDt = start.ToDateTime(TimeOnly.MinValue);
        query = query.Where(x => x.CreatedAt >= startDt);
    }

    if (!string.IsNullOrWhiteSpace(endDate) && DateOnly.TryParse(endDate, out var end))
    {
        var endDt = end.ToDateTime(TimeOnly.MaxValue);
        query = query.Where(x => x.CreatedAt <= endDt);
    }

    var total = await query.CountAsync(cancellationToken);

    var items = await query
        .OrderByDescending(x => x.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new
        {
            x.Id,
            x.OperatorName,
            x.ActionType,
            x.TargetType,
            x.TargetId,
            x.Remark,
            x.CreatedAt
        })
        .ToListAsync(cancellationToken);

    return ApiResponse.Ok(PagedResult<object>.Create(page, pageSize, total, items), "获取审计日志成功");
}).RequireAuthorization("SystemAdminOnly");

// ============ 员工端：我的班表 ============
// 允许 EMPLOYEE 及绑定了员工档案的 STORE_MANAGER/SYSTEM_ADMIN 访问（通过工号关联）
api.MapGet("/employee/my-schedule", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    string? month = null,
    string? employeeNo = null,
    CancellationToken cancellationToken = default) =>
{
    // 管理员/店长可通过 employeeNo 参数预览指定员工的班表
    var username = currentUser.Role == "EMPLOYEE"
        ? currentUser.Username
        : (string.IsNullOrWhiteSpace(employeeNo) ? currentUser.Username : employeeNo.Trim());
    if (string.IsNullOrWhiteSpace(username))
    {
        throw new UnauthorizedBusinessException("无法识别当前员工");
    }

    // 按工号关联员工（支持 EMPLOYEE 与 STORE_MANAGER 共用的店长账号）
    var employee = await dbContext.Employees
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == currentUser.StoreId && x.Status == 1, cancellationToken)
        ?? throw new NotFoundException("员工档案不存在");

    if (currentUser.Role != "EMPLOYEE" && currentUser.Role != "STORE_MANAGER" && currentUser.Role != "SYSTEM_ADMIN")
    {
        throw new BusinessException("无权限访问我的班表", "FORBIDDEN");
    }

    DateOnly from;
    DateOnly to;
    if (!string.IsNullOrWhiteSpace(month) && DateOnly.TryParse(month + "-01", out var m))
    {
        from = m;
        to = m.AddMonths(1).AddDays(-1);
    }
    else
    {
        // 默认近 30 天
        to = DateOnly.FromDateTime(DateTime.Today);
        from = to.AddDays(-30);
    }

    var plans = await dbContext.SchedulePlans
        .AsNoTracking()
        .Where(x => x.StoreId == employee.StoreId && x.Status == "PUBLISHED" &&
                    x.EndDate >= from && x.StartDate <= to)
        .OrderBy(x => x.StartDate)
        .ToListAsync(cancellationToken);

    var shiftCodes = await dbContext.ShiftTemplates
        .AsNoTracking()
        .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

    var planIds = plans.Select(p => p.Id).ToList();

    var summaries = await dbContext.ScheduleSummaries
        .AsNoTracking()
        .Where(x => planIds.Contains(x.PlanId) && x.EmployeeId == employee.Id)
        .OrderBy(x => x.WorkDate)
        .ToListAsync(cancellationToken);

    // 班中休息顶岗人姓名
    var coverIds = summaries
        .Where(x => x.BreakCoverEmployeeId is not null)
        .Select(x => x.BreakCoverEmployeeId!.Value)
        .Distinct()
        .ToList();
    var coverNames = coverIds.Count == 0
        ? new Dictionary<long, string>()
        : await dbContext.Employees
            .AsNoTracking()
            .Where(x => coverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    var byPlan = summaries
        .GroupBy(x => x.PlanId)
        .ToDictionary(g => g.Key, g => g.ToList());

    var result = plans.Select(p => new
    {
        p.Id,
        p.PlanName,
        p.StartDate,
        p.EndDate,
        Days = (byPlan.TryGetValue(p.Id, out var dayList) ? dayList : new List<ScheduleSummaryEntity>())
            .Select(s => new
            {
                s.WorkDate,
                s.IsRestDay,
                ShiftCode = s.ShiftTemplateId is null ? null : shiftCodes.GetValueOrDefault(s.ShiftTemplateId.Value),
                s.StartTime,
                s.EndTime,
                s.WorkHours,
                s.BreakStartTime,
                s.BreakEndTime,
                CoverEmployeeName = s.BreakCoverEmployeeId is null ? null : coverNames.GetValueOrDefault(s.BreakCoverEmployeeId.Value)
            })
            .ToList()
    }).ToList();

    // 我顶岗他人的记录（借调视角）
    var coverRows = await dbContext.ScheduleSummaries
        .AsNoTracking()
        .Where(x => planIds.Contains(x.PlanId) && x.BreakCoverEmployeeId == employee.Id && x.BreakStartTime != null)
        .Select(x => new { x.WorkDate, x.BreakStartTime, x.BreakEndTime, x.EmployeeId, x.BreakWorkstationId })
        .OrderBy(x => x.WorkDate)
        .ToListAsync(cancellationToken);

    var coverEmpIds = coverRows.Select(x => x.EmployeeId).Distinct().ToList();
    var coverEmpNames = coverEmpIds.Count == 0
        ? new Dictionary<long, string>()
        : await dbContext.Employees.AsNoTracking()
            .Where(x => coverEmpIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    var coverWsIds = coverRows.Where(x => x.BreakWorkstationId is not null)
        .Select(x => x.BreakWorkstationId!.Value).Distinct().ToList();
    var coverWsNames = coverWsIds.Count == 0
        ? new Dictionary<long, string>()
        : await dbContext.Workstations.AsNoTracking()
            .Where(x => coverWsIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    var covers = coverRows.Select(x => new
    {
        x.WorkDate,
        x.BreakStartTime,
        x.BreakEndTime,
        ForEmployeeName = coverEmpNames.GetValueOrDefault(x.EmployeeId, $"员工{x.EmployeeId}"),
        WorkstationName = x.BreakWorkstationId is null ? null : coverWsNames.GetValueOrDefault(x.BreakWorkstationId.Value)
    }).ToList();

    return ApiResponse.Ok(new
    {
        Employee = new { employee.Id, employee.EmployeeNo, employee.Name, employee.Department },
        Plans = result,
        Covers = covers
    }, "获取我的班表成功");
}).RequireAuthorization();

// 员工：获取可换班的已发布排班计划（仅登录即可，不限制管理员）
api.MapGet("/employee/swap-plans", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    CancellationToken ct) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var plans = await db.SchedulePlans.AsNoTracking()
        .Where(x => x.StoreId == storeId && x.Status == "PUBLISHED")
        .OrderByDescending(x => x.StartDate)
        .Select(x => new { x.Id, x.PlanName, x.StartDate, x.EndDate, x.Status })
        .ToListAsync(ct);

    return ApiResponse.Ok(plans, "获取可换班排班计划成功");
}).RequireAuthorization();

// ============ 请假申请 ============
// 员工提交请假（支持 EMPLOYEE + STORE_MANAGER + SYSTEM_ADMIN，通过工号关联员工档案）
api.MapPost("/leave-requests", async (
    LeaveRequestCreate request,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    IAuditLogService audit,
    CancellationToken ct) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var username = currentUser.Username;
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == currentUser.StoreId && x.Status == 1, ct)
        ?? throw new NotFoundException("员工档案不存在");

    if (currentUser.Role != "EMPLOYEE" && currentUser.Role != "STORE_MANAGER" && currentUser.Role != "SYSTEM_ADMIN")
        throw new BusinessException("无权限提交请假", "FORBIDDEN");

    if (request.StartDate > request.EndDate)
        throw new BusinessException("开始日期不能晚于结束日期", "INVALID_LEAVE");

    if (request.EndDate.DayNumber - request.StartDate.DayNumber > 30)
        throw new BusinessException("单次请假时长不能超过 30 天", "INVALID_LEAVE");

    if (request.StartDate < DateOnly.FromDateTime(DateTime.Today))
        throw new BusinessException("请假开始日期不能早于今天", "INVALID_LEAVE");

    // 重复请假校验：同一时间段已有 PENDING/APPROVED 请假
    var overlappingLeave = await db.LeaveRequests.AnyAsync(x =>
        x.EmployeeId == emp.Id &&
        x.Status != "REJECTED" &&
        x.StartDate <= request.EndDate &&
        x.EndDate >= request.StartDate, ct);
    if (overlappingLeave)
        throw new BusinessException("该时间段已有请假申请，请勿重复提交", "OVERLAPPING_LEAVE");

    // 与已发布排班冲突校验：请假日期内该员工有上班记录则提示
    var conflictDays = await db.ScheduleSummaries.AsNoTracking()
        .Where(x => x.StoreId == emp.StoreId
            && x.EmployeeId == emp.Id
            && x.IsRestDay == 0
            && x.WorkDate >= request.StartDate
            && x.WorkDate <= request.EndDate
            && db.SchedulePlans.Any(p => p.Id == x.PlanId && p.StoreId == emp.StoreId && p.Status == "PUBLISHED"))
        .Select(x => x.WorkDate)
        .Distinct()
        .ToListAsync(ct);

    if (conflictDays.Count > 0)
        throw new BusinessException($"请假日期与已发布排班的上班安排冲突：" +
            $"{string.Join("、", conflictDays.Take(5).Select(d => d.ToString("yyyy-MM-dd")))}" +
            (conflictDays.Count > 5 ? $" 等 {conflictDays.Count} 天" : ""), "LEAVE_CONFLICT_WITH_SCHEDULE");

    var leave = new LeaveRequestEntity
    {
        StoreId = emp.StoreId,
        EmployeeId = emp.Id,
        LeaveType = request.LeaveType ?? "PERSONAL",
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        Reason = request.Reason,
        Status = "PENDING",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.LeaveRequests.Add(leave);
    await db.SaveChangesAsync(ct);

    await audit.WriteAsync(emp.StoreId, currentUser.UserId, currentUser.Nickname, "CREATE_LEAVE", "LEAVE_REQUEST", leave.Id,
        null, $"{emp.Name} 请假 {request.StartDate:yyyy-MM-dd}~{request.EndDate:yyyy-MM-dd}", "提交请假", ct);

    // 通知管理员新请假申请
    db.Notifications.Add(new NotificationEntity
    {
        StoreId = emp.StoreId,
        ReceiverUserId = null,
        ReceiverEmployeeId = null,
        NotificationType = "NEW_LEAVE",
        Title = "新的请假申请",
        Content = $"{WebUtility.HtmlEncode(emp.Name)}({WebUtility.HtmlEncode(emp.EmployeeNo)}) 提交了 {request.StartDate:yyyy-MM-dd} 至 {request.EndDate:yyyy-MM-dd} 的请假申请",
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync(ct);

    return ApiResponse.Ok(new { leave.Id, leave.Status }, "请假申请已提交");
}).RequireAuthorization();

// 员工：我的请假列表（支持 EMPLOYEE + STORE_MANAGER + SYSTEM_ADMIN，管理员可用 employeeNo 预览）
api.MapGet("/leave-requests/mine", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    string? employeeNo = null,
    CancellationToken ct = default) =>
{
    var username = currentUser.Role == "EMPLOYEE"
        ? currentUser.Username
        : (string.IsNullOrWhiteSpace(employeeNo) ? currentUser.Username : employeeNo.Trim());
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == currentUser.StoreId && x.Status == 1, ct)
        ?? throw new NotFoundException("员工档案不存在");

    if (currentUser.Role != "EMPLOYEE" && currentUser.Role != "STORE_MANAGER" && currentUser.Role != "SYSTEM_ADMIN")
        throw new BusinessException("无权限查看我的请假", "FORBIDDEN");

    var items = await db.LeaveRequests.AsNoTracking()
        .Where(x => x.EmployeeId == emp.Id)
        .OrderByDescending(x => x.CreatedAt)
        .ToListAsync(ct);

    var result = items.Select(x => new { x.Id, x.LeaveType, x.StartDate, x.EndDate, x.Reason, x.Status, x.ReviewRemark, x.CreatedAt }).ToList();
    return ApiResponse.Ok(result, "获取我的请假成功");
}).RequireAuthorization();

// 员工：提前返岗（缩短已批准请假，仅本人可操作）
api.MapPut("/leave-requests/{id:long}/early-return", async (
    long id,
    EarlyReturnRequest request,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    IAuditLogService audit,
    CancellationToken ct) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var username = currentUser.Username;
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == currentUser.StoreId && x.Status == 1, ct)
        ?? throw new NotFoundException("员工档案不存在");

    var leave = await db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == emp.Id && x.StoreId == emp.StoreId, ct)
        ?? throw new NotFoundException("请假申请不存在");

    // 仅已批准的请假允许提前返岗
    if (leave.Status != "APPROVED")
        throw new BusinessException("仅已批准的请假可以提前返岗", "LEAVE_NOT_APPROVED");

    if (request.ReturnDate <= leave.StartDate)
        throw new BusinessException("返岗日期必须晚于请假开始日期", "INVALID_RETURN_DATE");

    if (request.ReturnDate >= leave.EndDate)
        throw new BusinessException("返岗日期必须早于原请假结束日期", "INVALID_RETURN_DATE");

    var oldEnd = leave.EndDate;
    leave.EndDate = request.ReturnDate;
    leave.EarlyReturned = 1;  // 标记提前返岗，供员工管理界面识别
    leave.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);

    await audit.WriteAsync(leave.StoreId, currentUser.UserId, currentUser.Nickname, "EARLY_RETURN_LEAVE", "LEAVE_REQUEST", leave.Id,
        oldEnd.ToString("yyyy-MM-dd"), request.ReturnDate.ToString("yyyy-MM-dd"),
        $"{emp.Name} 提前返岗：请假结束日期从 {oldEnd:yyyy-MM-dd} 改为 {request.ReturnDate:yyyy-MM-dd}", ct);

    // 通知管理员
    db.Notifications.Add(new NotificationEntity
    {
        StoreId = leave.StoreId,
        ReceiverUserId = null,
        ReceiverEmployeeId = null,
        NotificationType = "LEAVE_EARLY_RETURN",
        Title = "员工提前返岗",
        Content = $"{WebUtility.HtmlEncode(emp.Name)}({WebUtility.HtmlEncode(emp.EmployeeNo)}) 提前返岗，请假从 {leave.StartDate:yyyy-MM-dd} 至 {leave.EndDate:yyyy-MM-dd}",
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync(ct);

    return ApiResponse.Ok(new { leave.Id, leave.StartDate, leave.EndDate, leave.Status }, "已更新为提前返岗");
}).RequireAuthorization();

// 管理员：请假审批列表
api.MapGet("/leave-requests/review", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    string? status = null,
    CancellationToken ct = default) =>
{
    if (currentUser.Role == "EMPLOYEE")
        throw new BusinessException("无权限", "FORBIDDEN");

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var query = db.LeaveRequests.AsNoTracking().Where(x => x.StoreId == storeId);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);

    var items = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    var empIds = items.Select(x => x.EmployeeId).Distinct().ToList();
    var empMap = await db.Employees.AsNoTracking()
        .Where(x => empIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => new { x.EmployeeNo, x.Name, x.Department }, ct);

    var result = items.Select(x => new
    {
        x.Id, x.LeaveType, x.StartDate, x.EndDate, x.Reason, x.Status, x.ReviewRemark, x.EarlyReturned, x.CreatedAt,
        Employee = empMap.TryGetValue(x.EmployeeId, out var e) ? e : null
    }).ToList();

    return ApiResponse.Ok(result, "获取请假审批列表成功");
}).RequireAuthorization("AdminOnly");

// 管理员：审批（通过/驳回）
api.MapPut("/leave-requests/{id:long}/review", async (
    long id,
    LeaveReviewRequest review,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    IAuditLogService audit,
    CancellationToken ct) =>
{
    if (review is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    if (currentUser.Role == "EMPLOYEE")
        throw new BusinessException("无权限", "FORBIDDEN");

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var leave = await db.LeaveRequests.AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct)
        ?? throw new NotFoundException("请假申请不存在");

    if (leave.Status != "PENDING")
        throw new BusinessException("该申请已审批", "ALREADY_REVIEWED");

    var newStatus = review.Approved ? "APPROVED" : "REJECTED";
    var reviewTime = DateTime.UtcNow;

    // 原子抢占：仅当仍为 PENDING 时更新，防止并发重复审批（与换班审批一致）
    var claimed = await db.LeaveRequests
        .Where(x => x.Id == id && x.StoreId == storeId && x.Status == "PENDING")
        .ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, newStatus)
            .SetProperty(x => x.ReviewUserId, currentUser.UserId)
            .SetProperty(x => x.ReviewTime, reviewTime)
            .SetProperty(x => x.ReviewRemark, review.Remark)
            .SetProperty(x => x.UpdatedAt, reviewTime), ct);

    if (claimed == 0)
        throw new BusinessException("该申请已审批", "ALREADY_REVIEWED");

    await audit.WriteAsync(storeId, currentUser.UserId, currentUser.Nickname, "REVIEW_LEAVE", "LEAVE_REQUEST", leave.Id,
        null, $"审批请假 {leave.Id} -> {newStatus}", newStatus == "APPROVED" ? "批准请假" : "驳回请假", ct);

    // 通知员工审批结果
    var leaveEmp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == leave.EmployeeId, ct);
    if (leaveEmp != null)
    {
        var leaveNotif = new NotificationEntity
        {
            StoreId = storeId,
            ReceiverEmployeeId = leave.EmployeeId,
            NotificationType = newStatus == "APPROVED" ? "LEAVE_APPROVED" : "LEAVE_REJECTED",
            Title = newStatus == "APPROVED" ? "请假已批准" : "请假已驳回",
            Content = $"您的{leave.StartDate:yyyy-MM-dd}至{leave.EndDate:yyyy-MM-dd}的请假申请已被{(newStatus == "APPROVED" ? "批准" : "驳回")}",
            CreatedAt = DateTime.UtcNow
        };
        db.Notifications.Add(leaveNotif);
        await db.SaveChangesAsync(ct);
    }

    return ApiResponse.Ok(new { leave.Id, Status = newStatus }, newStatus == "APPROVED" ? "已批准" : "已驳回");
}).RequireAuthorization("AdminOnly");

// ============ 换班申请 ============
// 员工提交换班申请（支持 EMPLOYEE + STORE_MANAGER + SYSTEM_ADMIN，通过工号关联员工档案）
api.MapPost("/shift-swaps", async (
    ShiftSwapCreate request,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    IAuditLogService audit,
    CancellationToken ct) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    var username = currentUser.Username;
    var requesterEmp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == currentUser.StoreId && x.Status == 1, ct)
        ?? throw new NotFoundException("员工档案不存在");

    if (currentUser.Role != "EMPLOYEE" && currentUser.Role != "STORE_MANAGER" && currentUser.Role != "SYSTEM_ADMIN")
        throw new BusinessException("无权限提交换班", "FORBIDDEN");

    if (request.TargetEmployeeId <= 0)
        throw new BusinessException("请选择换班同事", "INVALID_SWAP");

    if (request.TargetEmployeeId == requesterEmp.Id)
        throw new BusinessException("不能选择自己作为换班同事", "INVALID_SWAP");

    var targetEmp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.TargetEmployeeId && x.StoreId == requesterEmp.StoreId && x.Status == 1, ct)
        ?? throw new NotFoundException("同伴员工档案不存在");

    // 校验排班计划存在且已发布
    var plan = await db.SchedulePlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.PlanId && x.StoreId == requesterEmp.StoreId && x.Status == "PUBLISHED", ct)
        ?? throw new NotFoundException("排班计划不存在或未发布");

    // 校验换班日期在计划范围内
    if (request.SwapDate < plan.StartDate || request.SwapDate > plan.EndDate)
        throw new BusinessException("换班日期不在排班计划范围内", "INVALID_SWAP");

    // 校验当天二人都是上班状态
    var summaries = await db.ScheduleSummaries.AsNoTracking()
        .Where(x => x.PlanId == request.PlanId && x.WorkDate == request.SwapDate &&
                    (x.EmployeeId == requesterEmp.Id || x.EmployeeId == request.TargetEmployeeId))
        .ToListAsync(ct);

    var requesterDay = summaries.FirstOrDefault(x => x.EmployeeId == requesterEmp.Id);
    var targetDay = summaries.FirstOrDefault(x => x.EmployeeId == request.TargetEmployeeId);

    if (requesterDay == null || requesterDay.IsRestDay == 1)
        throw new BusinessException("您当天不存在上班记录，无法换班", "INVALID_SWAP");
    if (targetDay == null || targetDay.IsRestDay == 0)
        throw new BusinessException("同伴当天也在上班，换班没有意义。请选择当天休息的同事", "INVALID_SWAP");

    // 不允许重复提交
    var pendingSwap = await db.ShiftSwaps.AnyAsync(
        x => x.RequesterEmployeeId == requesterEmp.Id
             && x.TargetEmployeeId == request.TargetEmployeeId
             && x.PlanId == request.PlanId
             && x.SwapDate == request.SwapDate
             && x.Status == "PENDING", ct);
    if (pendingSwap)
        throw new BusinessException("已存在相同的待审批换班申请", "DUPLICATE_SWAP");

    var swap = new ShiftSwapEntity
    {
        StoreId = requesterEmp.StoreId,
        PlanId = request.PlanId,
        RequesterEmployeeId = requesterEmp.Id,
        TargetEmployeeId = request.TargetEmployeeId,
        SwapDate = request.SwapDate,
        Reason = request.Reason,
        Status = "PENDING",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.ShiftSwaps.Add(swap);
    await db.SaveChangesAsync(ct);

    await audit.WriteAsync(requesterEmp.StoreId, currentUser.UserId, currentUser.Nickname, "CREATE_SWAP", "SHIFT_SWAP", swap.Id,
        null, $"{requesterEmp.Name} 申请与 {targetEmp.Name} 换班 {request.SwapDate:yyyy-MM-dd}", "提交换班", ct);

    // 通知管理员新换班申请
    db.Notifications.Add(new NotificationEntity
    {
        StoreId = requesterEmp.StoreId,
        ReceiverUserId = null,
        ReceiverEmployeeId = null,
        NotificationType = "NEW_SWAP",
        Title = "新的换班申请",
        Content = $"{WebUtility.HtmlEncode(requesterEmp.Name)}({WebUtility.HtmlEncode(requesterEmp.EmployeeNo)}) 申请与 {WebUtility.HtmlEncode(targetEmp.Name)}({WebUtility.HtmlEncode(targetEmp.EmployeeNo)}) 换班 {request.SwapDate:yyyy-MM-dd}",
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync(ct);

    return ApiResponse.Ok(new { swap.Id, swap.Status }, "换班申请已提交");
}).RequireAuthorization();

// 员工：我的换班列表（支持 EMPLOYEE + STORE_MANAGER + SYSTEM_ADMIN，管理员可用 employeeNo 预览）
api.MapGet("/shift-swaps/mine", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    string? employeeNo = null,
    CancellationToken ct = default) =>
{
    var username = currentUser.Role == "EMPLOYEE"
        ? currentUser.Username
        : (string.IsNullOrWhiteSpace(employeeNo) ? currentUser.Username : employeeNo.Trim());
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == currentUser.StoreId && x.Status == 1, ct)
        ?? throw new NotFoundException("员工档案不存在");

    if (currentUser.Role != "EMPLOYEE" && currentUser.Role != "STORE_MANAGER" && currentUser.Role != "SYSTEM_ADMIN")
        throw new BusinessException("无权限查看我的换班", "FORBIDDEN");

    var items = await db.ShiftSwaps.AsNoTracking()
        .Where(x => x.RequesterEmployeeId == emp.Id)
        .OrderByDescending(x => x.CreatedAt)
        .ToListAsync(ct);

    var empIds = items.SelectMany(x => new[] { x.RequesterEmployeeId, x.TargetEmployeeId }).Distinct().ToList();
    var empMap = await db.Employees.AsNoTracking()
        .Where(x => empIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => new { x.EmployeeNo, x.Name, x.Department }, ct);

    var result = items.Select(x => new
    {
        x.Id, x.PlanId, x.SwapDate, x.Reason, x.Status, x.ReviewRemark, x.CreatedAt,
        Requester = empMap.TryGetValue(x.RequesterEmployeeId, out var re) ? re : null,
        Target = empMap.TryGetValue(x.TargetEmployeeId, out var te) ? te : null
    }).ToList();
    return ApiResponse.Ok(result, "获取我的换班成功");
}).RequireAuthorization();

// 员工：获取可换班的同事列表（支持 EMPLOYEE + STORE_MANAGER + SYSTEM_ADMIN）
api.MapPost("/shift-swaps/candidates", async (HttpContext httpCtx) =>
{
    var db = httpCtx.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
    var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();

    var req = await httpCtx.Request.ReadFromJsonAsync<ShiftSwapCandidatesRequest>();
    if (currentUser.Role != "EMPLOYEE" && currentUser.Role != "STORE_MANAGER" && currentUser.Role != "SYSTEM_ADMIN")
        throw new BusinessException("无权限查看可换班同事", "FORBIDDEN");
    if (req == null || req.PlanId <= 0 || string.IsNullOrWhiteSpace(req.SwapDate))
        throw new BusinessException("请指定排班计划和换班日期", "INVALID_PARAM");
    if (!DateOnly.TryParse(req.SwapDate, out var date))
        throw new BusinessException("日期格式无效", "INVALID_PARAM");

    var username = currentUser.Username;
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.StoreId == currentUser.StoreId && x.Status == 1)
        ?? throw new NotFoundException("员工档案不存在");

    // 校验排班计划属于当前员工门店且已发布（防止跨门店信息泄露）
    var planExists = await db.SchedulePlans.AsNoTracking()
        .AnyAsync(x => x.Id == req.PlanId && x.StoreId == emp.StoreId && x.Status == "PUBLISHED");
    if (!planExists)
        throw new NotFoundException("排班计划不存在或未发布");

    var summaries = await db.ScheduleSummaries.AsNoTracking()
        .Where(x => x.PlanId == req.PlanId && x.WorkDate == date && x.IsRestDay == 1 && x.EmployeeId != emp.Id)
        .Select(x => x.EmployeeId)
        .Distinct()
        .ToListAsync();

    if (summaries.Count == 0)
    {
        httpCtx.Response.StatusCode = 200;
        await httpCtx.Response.WriteAsJsonAsync(ApiResponse.Ok(System.Array.Empty<object>(), "无可换班同事"));
        return;
    }

    var candidates = await db.Employees.AsNoTracking()
        .Where(x => summaries.Contains(x.Id) && x.Status == 1)
        .OrderBy(x => x.Department).ThenBy(x => x.Name)
        .Select(x => new { x.Id, x.EmployeeNo, x.Name, x.Department })
        .ToListAsync();

    httpCtx.Response.StatusCode = 200;
    await httpCtx.Response.WriteAsJsonAsync(ApiResponse.Ok(candidates, "获取可换班同事成功"));
}).RequireAuthorization();

// 管理员：换班审批列表
api.MapGet("/shift-swaps/review", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    string? status = null,
    CancellationToken ct = default) =>
{
    if (currentUser.Role == "EMPLOYEE")
        throw new BusinessException("无权限", "FORBIDDEN");

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var query = db.ShiftSwaps.AsNoTracking().Where(x => x.StoreId == storeId);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);

    var items = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    var empIds = items.SelectMany(x => new[] { x.RequesterEmployeeId, x.TargetEmployeeId }).Distinct().ToList();
    var empMap = await db.Employees.AsNoTracking()
        .Where(x => empIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => new { x.EmployeeNo, x.Name, x.Department }, ct);

    var result = items.Select(x => new
    {
        x.Id, x.PlanId, x.SwapDate, x.Reason, x.Status, x.ReviewRemark, x.CreatedAt,
        Requester = empMap.TryGetValue(x.RequesterEmployeeId, out var re) ? re : null,
        Target = empMap.TryGetValue(x.TargetEmployeeId, out var te) ? te : null
    }).ToList();

    return ApiResponse.Ok(result, "获取换班审批列表成功");
}).RequireAuthorization("AdminOnly");

// 管理员：审批换班（通过/驳回）
api.MapPut("/shift-swaps/{id:long}/review", async (
    long id,
    ShiftSwapReviewRequest review,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    IAuditLogService audit,
    CancellationToken ct) =>
{
    if (review is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    if (currentUser.Role == "EMPLOYEE")
        throw new BusinessException("无权限", "FORBIDDEN");

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var swap = await db.ShiftSwaps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct)
        ?? throw new NotFoundException("换班申请不存在");

    if (swap.Status != "PENDING")
        throw new BusinessException("该申请已审批", "ALREADY_REVIEWED");

    var newStatus = review.Approved ? "APPROVED" : "REJECTED";

    if (review.Approved)
    {
        // 执行换班：交换 schedule_results 和 schedule_summaries 中两人的 employee_id
        // 状态更新与数据交换必须在同一事务内，避免"数据已交换但状态仍 PENDING"的不一致
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 原子抢占状态：仅当仍为 PENDING 才置为 APPROVED，防止并发重复审批
            var claimed = await db.ShiftSwaps
                .Where(x => x.Id == id && x.StoreId == storeId && x.Status == "PENDING")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, "APPROVED")
                    .SetProperty(x => x.ReviewUserId, currentUser.UserId)
                    .SetProperty(x => x.ReviewTime, DateTime.UtcNow)
                    .SetProperty(x => x.ReviewRemark, review.Remark)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            if (claimed == 0)
            {
                await db.Database.RollbackTransactionAsync(ct);
                throw new BusinessException("该申请已审批", "ALREADY_REVIEWED");
            }

            // 交换 schedule_results（无唯一约束，直接互换）
            var resultsA = await db.ScheduleResults
                .Where(x => x.PlanId == swap.PlanId && x.WorkDate == swap.SwapDate && x.EmployeeId == swap.RequesterEmployeeId)
                .ToListAsync(ct);
            var resultsB = await db.ScheduleResults
                .Where(x => x.PlanId == swap.PlanId && x.WorkDate == swap.SwapDate && x.EmployeeId == swap.TargetEmployeeId)
                .ToListAsync(ct);

            foreach (var r in resultsA) r.EmployeeId = swap.TargetEmployeeId;
            foreach (var r in resultsB) r.EmployeeId = swap.RequesterEmployeeId;

            // 交换 schedule_summaries（有唯一约束 + 外键，用删除+重建避免冲突）
            var sumA = await db.ScheduleSummaries.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PlanId == swap.PlanId && x.WorkDate == swap.SwapDate && x.EmployeeId == swap.RequesterEmployeeId, ct);
            var sumB = await db.ScheduleSummaries.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PlanId == swap.PlanId && x.WorkDate == swap.SwapDate && x.EmployeeId == swap.TargetEmployeeId, ct);

            if (sumA != null) db.ScheduleSummaries.Remove(sumA);
            if (sumB != null) db.ScheduleSummaries.Remove(sumB);
            await db.SaveChangesAsync(ct);

            // 单向移交：申请人的班次转移给同伴，申请人变为休息
            if (sumA != null)
            {
                var newA = new ScheduleSummaryEntity
                {
                    PlanId = sumA.PlanId, StoreId = sumA.StoreId, EmployeeId = swap.TargetEmployeeId,
                    WorkDate = sumA.WorkDate, IsRestDay = sumA.IsRestDay, ShiftTemplateId = sumA.ShiftTemplateId,
                    StartTime = sumA.StartTime, EndTime = sumA.EndTime, WorkHours = sumA.WorkHours,
                    CoveredWorkstations = sumA.CoveredWorkstations,
                    BreakStartTime = sumA.BreakStartTime,
                    BreakEndTime = sumA.BreakEndTime,
                    BreakCoverEmployeeId = sumA.BreakCoverEmployeeId,
                    BreakWorkstationId = sumA.BreakWorkstationId
                };
                db.ScheduleSummaries.Add(newA);
            }
            // 申请人变为休息日
            var restForRequester = new ScheduleSummaryEntity
            {
                PlanId = swap.PlanId, StoreId = storeId, EmployeeId = swap.RequesterEmployeeId,
                WorkDate = swap.SwapDate, IsRestDay = 1, ShiftTemplateId = null,
                StartTime = null, EndTime = null, WorkHours = 0m,
                CoveredWorkstations = null
            };
            db.ScheduleSummaries.Add(restForRequester);
            await db.SaveChangesAsync(ct);

            await db.Database.CommitTransactionAsync(ct);
        }
        catch
        {
            await db.Database.RollbackTransactionAsync(ct);
            throw;
        }
    }
    else
    {
        var claimed = await db.ShiftSwaps
            .Where(x => x.Id == id && x.StoreId == storeId && x.Status == "PENDING")
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "REJECTED")
                .SetProperty(x => x.ReviewUserId, currentUser.UserId)
                .SetProperty(x => x.ReviewTime, DateTime.UtcNow)
                .SetProperty(x => x.ReviewRemark, review.Remark)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        if (claimed == 0)
            throw new BusinessException("该申请已审批", "ALREADY_REVIEWED");
    }

    // 通知换班员工（申请人 + 同伴）
    var swapNotifStatus = review.Approved ? "APPROVED" : "REJECTED";
    var swapNotifLabel = swapNotifStatus == "APPROVED" ? "批准" : "驳回";
    var swapNotifs = new[]
    {
        new NotificationEntity { StoreId = storeId, ReceiverEmployeeId = swap.RequesterEmployeeId, NotificationType = swapNotifStatus == "APPROVED" ? "SWAP_APPROVED" : "SWAP_REJECTED", Title = $"换班已{swapNotifLabel}", Content = $"您与同事的 {swap.SwapDate:yyyy-MM-dd} 换班申请已被{swapNotifLabel}", CreatedAt = DateTime.UtcNow },
        new NotificationEntity { StoreId = storeId, ReceiverEmployeeId = swap.TargetEmployeeId, NotificationType = swapNotifStatus == "APPROVED" ? "SWAP_APPROVED" : "SWAP_REJECTED", Title = $"换班已{swapNotifLabel}", Content = $"您与同事的 {swap.SwapDate:yyyy-MM-dd} 换班申请已被{swapNotifLabel}", CreatedAt = DateTime.UtcNow }
    };
    db.Notifications.AddRange(swapNotifs);
    await db.SaveChangesAsync(ct);

    await audit.WriteAsync(storeId, currentUser.UserId, currentUser.Nickname, "REVIEW_SWAP", "SHIFT_SWAP", swap.Id,
        null, $"审批换班 {swap.Id} -> {newStatus}", newStatus == "APPROVED" ? "批准换班" : "驳回换班", ct);

    return ApiResponse.Ok(new { swap.Id, Status = newStatus }, newStatus == "APPROVED" ? "换班已批准并生效" : "已驳回");
}).RequireAuthorization("AdminOnly");

// ============ 删除排班计划 ============
api.MapDelete("/schedules/{planId:long}", async (
    long planId,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var plan = await dbContext.SchedulePlans
        .FirstOrDefaultAsync(x => x.Id == planId && x.StoreId == storeId, cancellationToken)
        ?? throw new NotFoundException("排班计划不存在");

    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

    // 删除关联的明细、汇总、问题、换班记录（换班有外键引用排班计划）
    await dbContext.ShiftSwaps
        .Where(x => x.PlanId == planId)
        .ExecuteDeleteAsync(cancellationToken);

    await dbContext.ScheduleResults
        .Where(x => x.PlanId == planId)
        .ExecuteDeleteAsync(cancellationToken);

    await dbContext.ScheduleSummaries
        .Where(x => x.PlanId == planId)
        .ExecuteDeleteAsync(cancellationToken);

    await dbContext.ScheduleIssues
        .Where(x => x.PlanId == planId)
        .ExecuteDeleteAsync(cancellationToken);

    // 删除计划本身
    dbContext.SchedulePlans.Remove(plan);
    await dbContext.SaveChangesAsync(cancellationToken);

    await dbContext.Database.CommitTransactionAsync(cancellationToken);

    return ApiResponse.Ok(true, "删除排班计划成功");
}).RequireAuthorization("AdminOnly");

// ============ 排班问题（缺口/违规）标记 ============
api.MapGet("/schedules/{planId:long}/issues", async (
    long planId,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var planExists = await dbContext.SchedulePlans
        .AnyAsync(x => x.Id == planId && x.StoreId == storeId, cancellationToken);

    if (!planExists)
    {
        throw new NotFoundException("排班计划不存在");
    }

    var workstationInfo = await dbContext.Workstations
        .AsNoTracking()
        .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.IsLowSkill }, cancellationToken);

    var issues = await dbContext.ScheduleIssues
        .AsNoTracking()
        .Where(x => x.PlanId == planId && x.StoreId == storeId)
        .OrderBy(x => x.WorkDate)
        .ThenBy(x => x.TimeSlot)
        .Select(x => new
        {
            x.Id,
            x.IssueType,
            x.Severity,
            x.WorkDate,
            x.TimeSlot,
            x.EmployeeId,
            x.WorkstationId,
            x.Description
        })
        .ToListAsync(cancellationToken);

    var items = issues.Select(x => new
    {
        x.Id,
        x.IssueType,
        x.Severity,
        x.WorkDate,
        x.TimeSlot,
        x.EmployeeId,
        x.WorkstationId,
        WorkstationName = x.WorkstationId is null ? null : workstationInfo.GetValueOrDefault(x.WorkstationId.Value)?.Name,
        IsLowSkill = x.WorkstationId is not null && workstationInfo.TryGetValue(x.WorkstationId.Value, out var ws2) && ws2.IsLowSkill == 1,
        x.Description
    }).ToList();

    // 返回纯数组（兼容 week/day/loadIssues），合理度由独立端点 /rationality 提供
    return ApiResponse.Ok(items, "获取排班问题成功");
}).RequireAuthorization("AdminOnly");

// 每日排班合理度 = 当日实际排班人次 ÷ 当日需求人次 × 100
api.MapGet("/schedules/{planId:long}/rationality", async (
    long planId,
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var plan = await dbContext.SchedulePlans.AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == planId && x.StoreId == storeId, cancellationToken)
        ?? throw new NotFoundException("排班计划不存在");

    var dates = Enumerable.Range(0, plan.EndDate.DayNumber - plan.StartDate.DayNumber + 1)
        .Select(offset => plan.StartDate.AddDays(offset))
        .ToList();

    var dayTypeByDate = await dbContext.DateParameters.AsNoTracking()
        .Where(x => x.StoreId == storeId && x.WorkDate >= plan.StartDate && x.WorkDate <= plan.EndDate)
        .ToDictionaryAsync(x => x.WorkDate, x => x.DayType, cancellationToken);

    var requirements = await dbContext.StaffingRequirements.AsNoTracking()
        .Where(x => x.StoreId == storeId)
        .GroupBy(x => new { x.DayType, x.WorkstationId, x.TimeSlot })
        .Select(g => new { g.Key.DayType, g.Key.WorkstationId, g.Key.TimeSlot, Count = g.Max(x => x.RequiredCount) })
        .ToListAsync(cancellationToken);

    var coverages = await dbContext.ScheduleResults.AsNoTracking()
        .Where(x => x.PlanId == planId)
        .GroupBy(x => new { x.WorkDate, x.WorkstationId, x.TimeSlot })
        .Select(g => new { g.Key.WorkDate, g.Key.WorkstationId, g.Key.TimeSlot, Count = g.Select(x => x.EmployeeId).Distinct().Count() })
        .ToListAsync(cancellationToken);

    // 无人顶岗的班中休息：该时段该工作站覆盖人数 -1（有借调顶岗的不扣减）
    var uncoveredBreaks = (await dbContext.ScheduleSummaries.AsNoTracking()
            .Where(x => x.PlanId == planId && x.BreakStartTime != null &&
                        x.BreakCoverEmployeeId == null && x.BreakWorkstationId != null)
            .Select(x => new { x.WorkDate, x.BreakStartTime, x.BreakEndTime, x.BreakWorkstationId })
            .ToListAsync(cancellationToken))
        .Select(x => new { x.WorkDate, Start = x.BreakStartTime!.Value, End = x.BreakEndTime!.Value, Ws = x.BreakWorkstationId!.Value })
        .ToList();

    var rationality = new List<object>();
    foreach (var date in dates)
    {
        var dayType = dayTypeByDate.GetValueOrDefault(date) ?? "WORKDAY";
        // 营业日口径：凌晨时段（< 06:00）按前一天的日期类型取需求
        var prevType = dayTypeByDate.GetValueOrDefault(date.AddDays(-1)) ?? dayType;
        var dailyReqs = requirements
            .Where(r => r.DayType == (r.TimeSlot < TimeSpan.FromHours(6) ? prevType : dayType))
            .ToDictionary(r => (r.WorkstationId, r.TimeSlot), r => r.Count);
        var totalDemand = dailyReqs.Values.Sum();
        var covered = 0;
        foreach (var c in coverages.Where(c => c.WorkDate == date && c.WorkstationId.HasValue))
        {
            if (dailyReqs.TryGetValue((c.WorkstationId!.Value, c.TimeSlot), out var req))
            {
                // 休息覆盖该时段（含跨午夜回绕：End <= Start 视为跨午夜）
                var uncovered = uncoveredBreaks.Any(b =>
                    b.WorkDate == c.WorkDate && b.Ws == c.WorkstationId.Value &&
                    (b.End > b.Start
                        ? c.TimeSlot >= b.Start && c.TimeSlot < b.End
                        : c.TimeSlot >= b.Start || c.TimeSlot < b.End));
                var count = Math.Max(0, c.Count - (uncovered ? 1 : 0));
                covered += Math.Min(count, req);
            }
        }
        var pct = totalDemand > 0 ? (int)Math.Round(covered * 100.0 / totalDemand) : 100;
        rationality.Add(new { Date = date, Pct = Math.Min(100, Math.Max(0, pct)) });
    }

    return ApiResponse.Ok(rationality, "获取每日排班合理度成功");
}).RequireAuthorization("AdminOnly");

app.Run();

// ===== 请假 DTO =====
public sealed record LeaveRequestCreate(string? LeaveType, DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record EarlyReturnRequest(DateOnly ReturnDate);
public sealed record LeaveReviewRequest(bool Approved, string? Remark);

// ===== 换班 DTO =====
public sealed record ShiftSwapCreate(long PlanId, long TargetEmployeeId, DateOnly SwapDate, string? Reason);
public sealed record ShiftSwapCandidatesRequest(long PlanId, string SwapDate);
public sealed record ShiftSwapReviewRequest(bool Approved, string? Remark);

/// <summary>生成快照行（与 PreferenceService.ComputeAdjustmentsAsync 口径一致）。</summary>
internal sealed record SummarySnapshotRow(long EmployeeId, DateOnly WorkDate, int IsRestDay, long? ShiftId);

public partial class Program;
