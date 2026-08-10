using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Application.Auth;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Application.Employees;
using ShiftScheduling.Api.Application.EmployeeSkills;
using ShiftScheduling.Api.Application.RuleConfigs;
using ShiftScheduling.Api.Application.Schedules;
using ShiftScheduling.Api.Application.Security;
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
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IEmployeeSkillService, EmployeeSkillService>();
builder.Services.AddScoped<IWorkstationService, WorkstationService>();
builder.Services.AddScoped<IShiftTemplateService, ShiftTemplateService>();
builder.Services.AddScoped<IRuleConfigService, RuleConfigService>();
builder.Services.AddScoped<SchedulingEngine>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();

var connectionString = builder.Configuration.GetConnectionString("ShiftMvp");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("缺少数据库连接字符串 ConnectionStrings:ShiftMvp");
}

builder.Services.AddDbContext<ShiftSchedulingDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("缺少 JWT 配置");
if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) || string.IsNullOrWhiteSpace(jwtOptions.Audience))
{
    throw new InvalidOperationException("Jwt:Issuer 和 Jwt:Audience 不能为空");
}

if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey 至少需要 32 字节，请通过 User Secrets 或环境变量配置");
}

if (jwtOptions.ExpireMinutes is < 5 or > 1440)
{
    throw new InvalidOperationException("Jwt:ExpireMinutes 必须在 5 到 1440 分钟之间");
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

                var dbContext = context.HttpContext.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
                var userExists = await dbContext.Users
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == userId && x.Status == 1, context.HttpContext.RequestAborted);

                if (!userExists)
                {
                    context.Fail("用户不存在或已被停用");
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

builder.Services.AddAuthorization(options =>
{
    // 管理端策略：仅系统管理员与门店经理可访问管理接口
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("SYSTEM_ADMIN", "STORE_MANAGER"));
});
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/health", () => ApiResponse.Ok(new { status = "UP", service = "ShiftScheduling.Api" }, "后端服务运行正常"));

api.MapPost("/auth/login", async (LoginRequest request, IAuthService authService, CancellationToken cancellationToken) =>
{
    var result = await authService.LoginAsync(request, cancellationToken);
    return ApiResponse.Ok(result, "登录成功");
});

// 忘记密码：验证用户名 + 手机号后重置密码
api.MapPost("/auth/forgot-password", async (
    ForgotPasswordRequest request,
    IAuthService authService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new BusinessException("请求参数不能为空", "INVALID_REQUEST");
    }

    await authService.ForgotPasswordAsync(request, cancellationToken);
    return ApiResponse.Ok(true, "密码重置成功，请使用新密码登录");
});

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
    var shiftCount = await dbContext.ShiftTemplates.CountAsync(x => x.StoreId == storeId && x.Status == 1, cancellationToken);
    var workstationCount = await dbContext.Workstations.CountAsync(x => x.StoreId == storeId && x.Status == 1, cancellationToken);

    return ApiResponse.Ok(new { employeeCount, shiftCount, workstationCount }, "获取统计数据成功");
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
    if (page < 1 || pageSize is < 1 or > 100)
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

api.MapPut("/employees/{employeeId:long}/skills", async (
    long employeeId,
    EmployeeSkillSaveRequest request,
    ICurrentUser currentUser,
    IEmployeeSkillService employeeSkillService,
    CancellationToken cancellationToken) =>
{
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
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    var result = await ruleConfigService.UpdateAsync(
        id,
        request,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    return ApiResponse.Ok(result, "保存规则配置成功");
}).RequireAuthorization("AdminOnly");

// ============ 排班业务 ============
api.MapPost("/schedules/generate", async (
    GenerateScheduleRequest request,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
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
    if (page < 1 || pageSize is < 1 or > 100)
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

api.MapPost("/schedules/{planId:long}/publish", async (
    long planId,
    ICurrentUser currentUser,
    IScheduleService scheduleService,
    CancellationToken cancellationToken) =>
{
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    await scheduleService.PublishAsync(
        planId,
        storeId,
        currentUser.UserId ?? 0,
        currentUser.Nickname ?? currentUser.Username ?? "匿名",
        cancellationToken);

    // 生成排班发布通知
    var acc = app.Services.GetRequiredService<IHttpContextAccessor>();
    var ctx = acc.HttpContext!;
    var db = ctx.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
    var plan = await db.SchedulePlans.AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == planId && x.StoreId == storeId, cancellationToken);
    if (plan != null)
    {
        var employeeIds = await db.ScheduleSummaries.AsNoTracking()
            .Where(x => x.PlanId == planId)
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        db.Notifications.AddRange(employeeIds.Select(empId => new NotificationEntity
        {
            StoreId = storeId,
            ReceiverEmployeeId = empId,
            NotificationType = "SCHEDULE_PUBLISHED",
            Title = "排班已发布",
            Content = $"排班计划「{WebUtility.HtmlEncode(plan.PlanName)}」已发布，请查看您的班表",
            CreatedAt = DateTime.Now
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    return ApiResponse.Ok(true, "排班发布成功");
}).RequireAuthorization("AdminOnly");

// ============ 站内通知 ============
// 获取通知列表（管理端按门店，员工端按员工 ID）
api.MapGet("/notifications", async (HttpContext httpCtx) =>
{
    var db = httpCtx.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
    var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    IQueryable<NotificationEntity> query = db.Notifications.AsNoTracking().Where(x => x.StoreId == storeId);

    if (currentUser.Role == "EMPLOYEE")
    {
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == currentUser.Username && x.Status == 1);
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
        // 管理端显示门店下所有通知
    }

    var items = await query.OrderByDescending(x => x.CreatedAt)
        .Take(100)
        .Select(x => new { x.Id, x.NotificationType, x.Title, x.Content, x.IsRead, x.CreatedAt })
        .ToListAsync();

    httpCtx.Response.StatusCode = 200;
    await httpCtx.Response.WriteAsJsonAsync(ApiResponse.Ok(items, "获取通知列表成功"));
}).RequireAuthorization();

// 获取未读数量
api.MapGet("/notifications/unread-count", async (HttpContext httpCtx) =>
{
    var db = httpCtx.RequestServices.GetRequiredService<ShiftSchedulingDbContext>();
    var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    IQueryable<NotificationEntity> query = db.Notifications.AsNoTracking().Where(x => x.StoreId == storeId && x.IsRead == 0);

    if (currentUser.Role == "EMPLOYEE")
    {
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == currentUser.Username && x.Status == 1);
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
        // 管理端显示门店下所有通知
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

    var notification = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct)
        ?? throw new NotFoundException("通知不存在");

    notification.IsRead = 1;
    notification.ReadAt = DateTime.Now;
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
        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeNo == currentUser.Username && x.Status == 1, ct);
        if (emp != null)
            query = query.Where(x => x.ReceiverEmployeeId == emp.Id);
    }
    else
    {
        // 管理端显示门店下所有通知
    }

    var now = DateTime.Now;
    await query.ExecuteUpdateAsync(x => x.SetProperty(n => n.IsRead, 1).SetProperty(n => n.ReadAt, now), ct);
    return ApiResponse.Ok(true, "已全部标记为已读");
}).RequireAuthorization();

// ============ 审计日志 ============
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
    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");
    if (page < 1 || pageSize is < 1 or > 100)
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
}).RequireAuthorization("AdminOnly");

// ============ 员工端：我的班表 ============
// 允许 EMPLOYEE 及绑定了员工档案的 STORE_MANAGER/SYSTEM_ADMIN 访问（通过工号关联）
api.MapGet("/employee/my-schedule", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext dbContext,
    string? month = null,
    CancellationToken cancellationToken = default) =>
{
    var username = currentUser.Username;
    if (string.IsNullOrWhiteSpace(username))
    {
        throw new UnauthorizedBusinessException("无法识别当前员工");
    }

    // 按工号关联员工（支持 EMPLOYEE 与 STORE_MANAGER 共用的店长账号）
    var employee = await dbContext.Employees
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.EmployeeNo == username && x.Status == 1, cancellationToken)
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
        .OrderByDescending(x => x.StartDate)
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
                s.WorkHours
            })
            .ToList()
    }).ToList();

    return ApiResponse.Ok(new { Employee = new { employee.Id, employee.EmployeeNo, employee.Name, employee.Department }, Plans = result }, "获取我的班表成功");
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
    var username = currentUser.Username;
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.Status == 1, ct)
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
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
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
        CreatedAt = DateTime.Now
    });
    await db.SaveChangesAsync(ct);

    return ApiResponse.Ok(new { leave.Id, leave.Status }, "请假申请已提交");
}).RequireAuthorization();

// 员工：我的请假列表（支持 EMPLOYEE + STORE_MANAGER + SYSTEM_ADMIN）
api.MapGet("/leave-requests/mine", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    CancellationToken ct) =>
{
    var username = currentUser.Username;
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.Status == 1, ct)
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
        x.Id, x.LeaveType, x.StartDate, x.EndDate, x.Reason, x.Status, x.ReviewRemark, x.CreatedAt,
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
    if (currentUser.Role == "EMPLOYEE")
        throw new BusinessException("无权限", "FORBIDDEN");

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var leave = await db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct)
        ?? throw new NotFoundException("请假申请不存在");

    if (leave.Status != "PENDING")
        throw new BusinessException("该申请已审批", "ALREADY_REVIEWED");

    if (review.Approved)
    {
        leave.Status = "APPROVED";
    }
    else
    {
        leave.Status = "REJECTED";
    }
    leave.ReviewUserId = currentUser.UserId;
    leave.ReviewTime = DateTime.Now;
    leave.ReviewRemark = review.Remark;
    leave.UpdatedAt = DateTime.Now;

    await db.SaveChangesAsync(ct);

    await audit.WriteAsync(storeId, currentUser.UserId, currentUser.Nickname, "REVIEW_LEAVE", "LEAVE_REQUEST", leave.Id,
        null, $"审批请假 {leave.Id} -> {leave.Status}", leave.Status == "APPROVED" ? "批准请假" : "驳回请假", ct);

    // 通知员工审批结果
    var leaveEmp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == leave.EmployeeId, ct);
    if (leaveEmp != null)
    {
        var leaveNotif = new NotificationEntity
        {
            StoreId = storeId,
            ReceiverEmployeeId = leave.EmployeeId,
            NotificationType = leave.Status == "APPROVED" ? "LEAVE_APPROVED" : "LEAVE_REJECTED",
            Title = leave.Status == "APPROVED" ? "请假已批准" : "请假已驳回",
            Content = $"您的{leave.StartDate:yyyy-MM-dd}至{leave.EndDate:yyyy-MM-dd}的请假申请已被{(leave.Status == "APPROVED" ? "批准" : "驳回")}",
            CreatedAt = DateTime.Now
        };
        db.Notifications.Add(leaveNotif);
        await db.SaveChangesAsync(ct);
    }

    return ApiResponse.Ok(new { leave.Id, leave.Status }, leave.Status == "APPROVED" ? "已批准" : "已驳回");
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
    var username = currentUser.Username;
    var requesterEmp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.Status == 1, ct)
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
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
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
        CreatedAt = DateTime.Now
    });
    await db.SaveChangesAsync(ct);

    return ApiResponse.Ok(new { swap.Id, swap.Status }, "换班申请已提交");
}).RequireAuthorization();

// 员工：我的换班列表（支持 EMPLOYEE + STORE_MANAGER + SYSTEM_ADMIN）
api.MapGet("/shift-swaps/mine", async (
    ICurrentUser currentUser,
    ShiftSchedulingDbContext db,
    CancellationToken ct) =>
{
    var username = currentUser.Username;
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.Status == 1, ct)
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
    var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == username && x.Status == 1)
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
    if (currentUser.Role == "EMPLOYEE")
        throw new BusinessException("无权限", "FORBIDDEN");

    var storeId = currentUser.StoreId ?? throw new UnauthorizedBusinessException("当前用户未关联门店");

    var swap = await db.ShiftSwaps.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct)
        ?? throw new NotFoundException("换班申请不存在");

    if (swap.Status != "PENDING")
        throw new BusinessException("该申请已审批", "ALREADY_REVIEWED");

    swap.ReviewUserId = currentUser.UserId;
    swap.ReviewTime = DateTime.Now;
    swap.ReviewRemark = review.Remark;
    swap.UpdatedAt = DateTime.Now;

    if (review.Approved)
    {
        // 执行换班：交换 schedule_results 和 schedule_summaries 中两人的 employee_id
        // 状态更新与数据交换必须在同一事务内，避免"数据已交换但状态仍 PENDING"的不一致
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 先更新状态（行级锁持有至事务提交，防止并发重复审批）
            swap.Status = "APPROVED";
            await db.SaveChangesAsync(ct);

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
                    CoveredWorkstations = sumA.CoveredWorkstations
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
        swap.Status = "REJECTED";
        await db.SaveChangesAsync(ct);
    }

    // 通知换班员工（申请人 + 同伴）
    var swapNotifStatus = review.Approved ? "APPROVED" : "REJECTED";
    var swapNotifLabel = swapNotifStatus == "APPROVED" ? "批准" : "驳回";
    var swapNotifs = new[]
    {
        new NotificationEntity { StoreId = storeId, ReceiverEmployeeId = swap.RequesterEmployeeId, NotificationType = swapNotifStatus == "APPROVED" ? "SWAP_APPROVED" : "SWAP_REJECTED", Title = $"换班已{swapNotifLabel}", Content = $"您与同事的 {swap.SwapDate:yyyy-MM-dd} 换班申请已被{swapNotifLabel}", CreatedAt = DateTime.Now },
        new NotificationEntity { StoreId = storeId, ReceiverEmployeeId = swap.TargetEmployeeId, NotificationType = swapNotifStatus == "APPROVED" ? "SWAP_APPROVED" : "SWAP_REJECTED", Title = $"换班已{swapNotifLabel}", Content = $"您与同事的 {swap.SwapDate:yyyy-MM-dd} 换班申请已被{swapNotifLabel}", CreatedAt = DateTime.Now }
    };
    db.Notifications.AddRange(swapNotifs);
    await db.SaveChangesAsync(ct);

    await audit.WriteAsync(storeId, currentUser.UserId, currentUser.Nickname, "REVIEW_SWAP", "SHIFT_SWAP", swap.Id,
        null, $"审批换班 {swap.Id} -> {swap.Status}", swap.Status == "APPROVED" ? "批准换班" : "驳回换班", ct);

    return ApiResponse.Ok(new { swap.Id, swap.Status }, swap.Status == "APPROVED" ? "换班已批准并生效" : "已驳回");
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

    // 删除关联的明细、汇总、问题
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

    var workstationNames = await dbContext.Workstations
        .AsNoTracking()
        .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

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
        WorkstationName = x.WorkstationId is null ? null : workstationNames.GetValueOrDefault(x.WorkstationId.Value),
        x.Description
    }).ToList();

    return ApiResponse.Ok(items, "获取排班问题成功");
}).RequireAuthorization("AdminOnly");

app.Run();

// ===== 请假 DTO =====
public sealed record LeaveRequestCreate(string? LeaveType, DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record LeaveReviewRequest(bool Approved, string? Remark);

// ===== 换班 DTO =====
public sealed record ShiftSwapCreate(long PlanId, long TargetEmployeeId, DateOnly SwapDate, string? Reason);
public sealed record ShiftSwapCandidatesRequest(long PlanId, string SwapDate);
public sealed record ShiftSwapReviewRequest(bool Approved, string? Remark);

public partial class Program;
