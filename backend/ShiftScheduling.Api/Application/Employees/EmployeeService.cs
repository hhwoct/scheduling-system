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

    public async Task<PagedResult<EmployeeListItem>> QueryAsync(EmployeeQueryRequest request, long storeId, CancellationToken cancellationToken)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == (request.Status ?? 1));

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
            query = query.Where(x => x.Department == request.Department);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.EmployeeNo)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new EmployeeListItem(
                x.Id,
                x.EmployeeNo,
                x.Name,
                x.Phone,
                x.Department,
                x.HireDate,
                x.PrimaryPosition,
                x.MaxWeeklyHours,
                x.Status))
            .ToListAsync(cancellationToken);

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

        var employee = new EmployeeEntity
        {
            StoreId = storeId,
            EmployeeNo = normalizedEmployeeNo,
            Name = request.Name.Trim(),
            Phone = request.Phone,
            Department = request.Department.Trim(),
            HireDate = request.HireDate,
            PrimaryPosition = request.PrimaryPosition,
            MaxWeeklyHours = request.MaxWeeklyHours,
            Status = 1,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _dbContext.Employees.Add(employee);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "CREATE_EMPLOYEE",
            "EMPLOYEE",
            employee.Id,
            null,
            System.Text.Json.JsonSerializer.Serialize(new { employee.EmployeeNo, employee.Name, employee.Department }),
            "新增员工",
            cancellationToken);

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
        Validate(request);

        // 查重前先 Trim，避免"E001"与"E001 "被当作不同值
        var normalizedEmployeeNo = request.EmployeeNo.Trim();

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken);

        if (employee is null)
        {
            throw new NotFoundException("员工不存在");
        }

        var duplicate = await _dbContext.Employees
            .AnyAsync(x => x.StoreId == storeId && x.EmployeeNo == normalizedEmployeeNo && x.Id != id, cancellationToken);

        if (duplicate)
        {
            throw new BusinessException($"员工工号 {normalizedEmployeeNo} 已被其他员工使用", "EMPLOYEE_NO_EXISTS");
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { employee.EmployeeNo, employee.Name, employee.Department, employee.MaxWeeklyHours });

        employee.EmployeeNo = normalizedEmployeeNo;
        employee.Name = request.Name.Trim();
        employee.Phone = request.Phone;
        employee.Department = request.Department.Trim();
        employee.HireDate = request.HireDate;
        employee.PrimaryPosition = request.PrimaryPosition;
        employee.MaxWeeklyHours = request.MaxWeeklyHours;
        employee.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_EMPLOYEE",
            "EMPLOYEE",
            employee.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { employee.EmployeeNo, employee.Name, employee.Department, employee.MaxWeeklyHours }),
            "编辑员工",
            cancellationToken);

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
        employee.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "DEACTIVATE_EMPLOYEE",
            "EMPLOYEE",
            employee.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { employee.Status }),
            "停用员工",
            cancellationToken);
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

        if (request.MaxWeeklyHours <= 0 || request.MaxWeeklyHours > 168)
        {
            throw new BusinessException("最大周工时必须在 1 到 168 之间", "INVALID_EMPLOYEE");
        }
    }

    private static EmployeeDetail MapToDetail(EmployeeEntity employee)
        => new(
            employee.Id,
            employee.EmployeeNo,
            employee.Name,
            employee.Phone,
            employee.Department,
            employee.HireDate,
            employee.PrimaryPosition,
            employee.MaxWeeklyHours,
            employee.Status,
            employee.CreatedAt,
            employee.UpdatedAt);
}