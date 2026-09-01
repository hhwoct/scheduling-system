using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.Employees;

public sealed class EmployeeService : IEmployeeService
{
    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public EmployeeService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    /// <summary>同步查重（用于捕获 DbUpdateException 时的并发兜底判断）。</summary>
    private bool ExistsEmployeeNo(long storeId, string employeeNo)
        => _dbContext.Employees.Any(x => x.StoreId == storeId && x.EmployeeNo == employeeNo);

    public async Task<PagedResult<EmployeeListItem>> QueryAsync(EmployeeQueryRequest request, long? storeId, bool excludeParttime, CancellationToken cancellationToken)
    {
        // 修复 EF Core 无法比较 int 与 int?：将 nullable 提升为局部变量
        var defaultStatus = request.Status ?? 1;
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.Status == defaultStatus);

        // 数据范围:storeId 为 null 表示跨全部门店(超管);否则限定本店
        if (storeId.HasValue)
        {
            query = query.Where(x => x.StoreId == storeId.Value);
        }

        // 超管视角的员工列表不含兼职
        if (excludeParttime)
        {
            query = query.Where(x => x.IsParttime == 0);
        }

        if (request.StoreId.HasValue)
        {
            query = query.Where(x => x.StoreId == request.StoreId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            query = query.Where(x => x.Name.Contains(request.Name));
        }

        if (!string.IsNullOrWhiteSpace(request.EmployeeNo))
        {
            query = query.Where(x => x.EmployeeNo.Contains(request.EmployeeNo));
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            query = query.Where(x => x.Department.Contains(request.Department));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = (await query
            .OrderBy(x => x.EmployeeNo)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                Row = new EmployeeListItem(
                    x.Id,
                    x.EmployeeNo,
                    x.Name,
                    x.Phone,
                    x.Department,
                    x.HireDate,
                    x.PrimaryPosition,
                    x.MaxWeeklyHours,
                    x.WeeklyHoursFollowDefault,
                    x.IsParttime,
                    x.Status,
                    x.StoreId,
                    ""),
                StoreName = _dbContext.Stores.Where(s => s.Id == x.StoreId).Select(s => s.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken))
            .Select(x => x.Row with { Phone = MaskPhone(x.Row.Phone), StoreName = x.StoreName })
            .ToList();

        return PagedResult<EmployeeListItem>.Create(request.Page, request.PageSize, total, items);
    }

    public async Task<EmployeeDetail> GetByIdAsync(long id, long storeId, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken);

        if (employee is null)
        {
            throw new NotFoundException("员工不存在");
        }

        return MapToDetail(employee);
    }

    public async Task<EmployeeDetail> CreateAsync(
        EmployeeUpsertRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        Validate(request);

        // 查重前先 Trim，避免"E001"与"E001 "被当作不同值
        var normalizedEmployeeNo = request.EmployeeNo.Trim();

        var exists = await _dbContext.Employees
            .AnyAsync(x => x.StoreId == storeId && x.EmployeeNo == normalizedEmployeeNo, cancellationToken);

        if (exists)
        {
            throw new BusinessException($"员工工号 {normalizedEmployeeNo} 已存在", "EMPLOYEE_NO_EXISTS");
        }

        var followDefault = request.WeeklyHoursFollowDefault == 1;
        var effectiveMaxWeeklyHours = followDefault
            ? await GetGlobalMaxWeeklyHoursAsync(storeId, cancellationToken)
            : request.MaxWeeklyHours;

        var employee = new EmployeeEntity
        {
            StoreId = storeId,
            EmployeeNo = normalizedEmployeeNo,
            Name = request.Name.Trim(),
            Phone = request.Phone,
            Department = request.Department.Trim(),
            HireDate = request.HireDate,
            PrimaryPosition = request.PrimaryPosition,
            MaxWeeklyHours = effectiveMaxWeeklyHours,
            WeeklyHoursFollowDefault = followDefault ? 1 : 0,
            Status = 1,
            IsParttime = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Employees.Add(employee);

        // 修复：审计与业务数据在同一事务内提交（不再事后补写，避免"业务已提交、审计缺失"）。
        // 新增时自增主键未生成，target_id 置空（备注中已含工号）。
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "CREATE_EMPLOYEE",
            "EMPLOYEE",
            null,
            null,
            System.Text.Json.JsonSerializer.Serialize(new { employee.EmployeeNo, employee.Name, employee.Department, employee.MaxWeeklyHours, employee.WeeklyHoursFollowDefault }),
            "新增员工",
            DateTime.UtcNow);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (ExistsEmployeeNo(storeId, normalizedEmployeeNo))
        {
            // 并发兜底：查重与保存之间的竞态由数据库唯一索引拦截，转成友好错误
            throw new BusinessException($"员工工号 {normalizedEmployeeNo} 已存在", "EMPLOYEE_NO_EXISTS");
        }

        return MapToDetail(employee);
    }

    public async Task<EmployeeDetail> UpdateAsync(
        long id,
        EmployeeUpsertRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        // 先取现有档案：手机号脱敏回环保护需要原值
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken);

        if (employee is null)
        {
            throw new NotFoundException("员工不存在");
        }

        // 手机号脱敏回环保护：列表/详情接口返回脱敏值（如 138****0001），前端编辑表单
        // 回填后原样提交。提交值含脱敏标记（****）视为"未修改"，保留库中原手机号，
        // 避免脱敏值覆盖真实号码（此前会导致校验失败，编辑功能不可用）。
        if (IsMaskedPhone(request.Phone))
        {
            request = request with { Phone = employee.Phone };
        }

        Validate(request);

        // 查重前先 Trim，避免"E001"与"E001 "被当作不同值
        var normalizedEmployeeNo = request.EmployeeNo.Trim();

        var duplicate = await _dbContext.Employees
            .AnyAsync(x => x.StoreId == storeId && x.EmployeeNo == normalizedEmployeeNo && x.Id != id, cancellationToken);

        if (duplicate)
        {
            throw new BusinessException($"员工工号 {normalizedEmployeeNo} 已被其他员工使用", "EMPLOYEE_NO_EXISTS");
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { employee.EmployeeNo, employee.Name, employee.Department, employee.MaxWeeklyHours, employee.WeeklyHoursFollowDefault });

        employee.EmployeeNo = normalizedEmployeeNo;
        employee.Name = request.Name.Trim();
        employee.Phone = request.Phone;
        employee.Department = request.Department.Trim();
        employee.HireDate = request.HireDate;
        employee.PrimaryPosition = request.PrimaryPosition;
        var followDefault = request.WeeklyHoursFollowDefault == 1;
        employee.MaxWeeklyHours = followDefault
            ? await GetGlobalMaxWeeklyHoursAsync(storeId, cancellationToken)
            : request.MaxWeeklyHours;
        employee.WeeklyHoursFollowDefault = followDefault ? 1 : 0;
        employee.UpdatedAt = DateTime.UtcNow;

        // 修复：审计与业务数据在同一事务内提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_EMPLOYEE",
            "EMPLOYEE",
            employee.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { employee.EmployeeNo, employee.Name, employee.Department, employee.MaxWeeklyHours, employee.WeeklyHoursFollowDefault }),
            "编辑员工",
            DateTime.UtcNow);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (ExistsEmployeeNo(storeId, normalizedEmployeeNo))
        {
            // 并发兜底：查重与保存之间的竞态由数据库唯一索引拦截，转成友好错误
            throw new BusinessException($"员工工号 {normalizedEmployeeNo} 已被其他员工使用", "EMPLOYEE_NO_EXISTS");
        }

        return MapToDetail(employee);
    }

    public async Task DeactivateAsync(
        long id,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken);

        if (employee is null)
        {
            throw new NotFoundException("员工不存在");
        }

        if (employee.Status == 0)
        {
            throw new BusinessException("员工已是停用状态", "EMPLOYEE_ALREADY_INACTIVE");
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { employee.Status });

        employee.Status = 0;
        employee.UpdatedAt = DateTime.UtcNow;

        // 修复：审计与业务数据在同一事务内提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "DEACTIVATE_EMPLOYEE",
            "EMPLOYEE",
            employee.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { employee.Status }),
            "停用员工",
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(EmployeeUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeNo))
        {
            throw new BusinessException("工号不能为空", "INVALID_EMPLOYEE");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BusinessException("姓名不能为空", "INVALID_EMPLOYEE");
        }

        if (string.IsNullOrWhiteSpace(request.Department))
        {
            throw new BusinessException("部门不能为空", "INVALID_EMPLOYEE");
        }

        if (request.WeeklyHoursFollowDefault == 0 && (request.MaxWeeklyHours <= 0 || request.MaxWeeklyHours > 168))
        {
            throw new BusinessException("最大周工时必须在 1 到 168 之间", "INVALID_EMPLOYEE");
        }

        // 手机号格式校验（忘记密码按「员工姓名+手机号」精确匹配，脏数据将导致无法自助重置密码）
        if (!string.IsNullOrWhiteSpace(request.Phone) &&
            !System.Text.RegularExpressions.Regex.IsMatch(request.Phone.Trim(), "^1[0-9]{10}$"))
        {
            throw new BusinessException("手机号格式不正确（需 11 位数字，以 1 开头）", "INVALID_EMPLOYEE");
        }
    }

    private static EmployeeDetail MapToDetail(EmployeeEntity employee)
        => new(
            employee.Id,
            employee.EmployeeNo,
            employee.Name,
            MaskPhone(employee.Phone),
            employee.Department,
            employee.HireDate,
            employee.PrimaryPosition,
            employee.MaxWeeklyHours,
            employee.WeeklyHoursFollowDefault,
            employee.IsParttime,
            employee.Status,
            employee.CreatedAt,
            employee.UpdatedAt);

    /// <summary>
    /// 读取门店当前「最大周工时」全局规则值；缺省或非法时回退 48。
    /// </summary>
    private async Task<decimal> GetGlobalMaxWeeklyHoursAsync(long storeId, CancellationToken cancellationToken)
    {
        var ruleValue = await _dbContext.RuleConfigs
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.RuleKey == "max_weekly_hours" && x.Status == 1)
            .Select(x => x.RuleValue)
            .FirstOrDefaultAsync(cancellationToken);

        return decimal.TryParse(ruleValue, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 48m;
    }

    /// <summary>
    /// 判断手机号是否为脱敏值（MaskPhone 的输出含 ****）。
    /// 真实手机号经格式校验不可能含 *，故可作为"未修改"标记。
    /// </summary>
    private static bool IsMaskedPhone(string? phone)
        => !string.IsNullOrWhiteSpace(phone) && phone.Contains("****", StringComparison.Ordinal);

    /// <summary>
    /// 手机号脱敏：保留前 3 位与后 4 位，中间以 **** 代替（如 138****1234）。
    /// </summary>
    private static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return phone;
        }

        var p = phone.Trim();
        if (p.Length <= 7)
        {
            return p[..1] + "****";
        }

        return p[..3] + "****" + p[^4..];
    }
}