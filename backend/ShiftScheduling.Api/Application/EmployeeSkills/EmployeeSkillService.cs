using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.EmployeeSkills;

public sealed class EmployeeSkillService : IEmployeeSkillService
{
    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public EmployeeSkillService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<EmployeeSkillMatrix> GetByEmployeeAsync(long employeeId, long storeId, CancellationToken cancellationToken)
    {
        var employeeExists = await _dbContext.Employees
            .AnyAsync(x => x.Id == employeeId && x.StoreId == storeId, cancellationToken);

        if (!employeeExists)
        {
            throw new NotFoundException("员工不存在");
        }

        var skills = await _dbContext.EmployeeSkills
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.Status == 1)
            .Join(
                _dbContext.Workstations.AsNoTracking(),
                skill => skill.WorkstationId,
                workstation => workstation.Id,
                (skill, workstation) => new { skill, workstation })
            .OrderBy(x => x.workstation.Id)
            .Select(x => new EmployeeSkillItem(
                x.workstation.Id,
                x.workstation.Code,
                x.workstation.Name,
                x.skill.SkillScore,
                x.skill.IsPrimarySkill))
            .ToListAsync(cancellationToken);

        return new EmployeeSkillMatrix(employeeId, skills);
    }

    public async Task<SkillMatrixOverview> GetStoreMatrixAsync(long storeId, CancellationToken cancellationToken)
    {
        var workstations = await _dbContext.Workstations
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new SkillMatrixWorkstation(x.Id, x.Code, x.Name))
            .ToListAsync(cancellationToken);

        // 兼职员工不参与评级（其技能仍保留，供排班算法兼职替补使用）
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1 && x.IsParttime != 1)
            .OrderBy(x => x.EmployeeNo)
            .Select(x => new SkillMatrixEmployee(x.Id, x.EmployeeNo, x.Name, x.Department, x.IsParttime, x.IsGeneralist))
            .ToListAsync(cancellationToken);

        var cells = await _dbContext.EmployeeSkills
            .AsNoTracking()
            .Where(x => x.Status == 1)
            .Join(
                _dbContext.Employees.AsNoTracking().Where(e => e.StoreId == storeId && e.IsParttime != 1),
                s => s.EmployeeId,
                e => e.Id,
                (s, e) => new SkillMatrixCell(s.EmployeeId, s.WorkstationId, s.SkillScore, s.IsPrimarySkill))
            .ToListAsync(cancellationToken);

        return new SkillMatrixOverview(workstations, employees, cells);
    }

    public async Task<SkillMatrixCell> UpdateCellAsync(
        SkillMatrixCellUpdateRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(x => x.Id == request.EmployeeId && x.StoreId == storeId && x.Status == 1, cancellationToken)
            ?? throw new NotFoundException("员工不存在");

        var workstation = await _dbContext.Workstations
            .FirstOrDefaultAsync(x => x.Id == request.WorkstationId && x.StoreId == storeId && x.Status == 1, cancellationToken)
            ?? throw new NotFoundException("工作站不存在或已停用");

        if (request.SkillScore is < 0 or > 5)
        {
            throw new BusinessException("技能分必须在 0 到 5 之间", "INVALID_SKILL_SCORE");
        }

        // 0 分没有主技能意义，强制清掉主技能标记
        var isPrimary = request.SkillScore == 0 ? 0 : (request.IsPrimarySkill == 1 ? 1 : 0);

        var beforeContent = await BuildMatrixContentAsync(employee.Id, cancellationToken);

        var skill = await _dbContext.EmployeeSkills
            .FirstOrDefaultAsync(x => x.EmployeeId == employee.Id && x.WorkstationId == workstation.Id, cancellationToken);

        if (isPrimary == 1)
        {
            // 同一员工只允许一个主技能：先清掉其他工作站的主技能标记
            var otherPrimaries = await _dbContext.EmployeeSkills
                .Where(x => x.EmployeeId == employee.Id && x.WorkstationId != workstation.Id && x.IsPrimarySkill == 1)
                .ToListAsync(cancellationToken);
            foreach (var other in otherPrimaries)
            {
                other.IsPrimarySkill = 0;
                other.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (skill is null)
        {
            skill = new EmployeeSkillEntity
            {
                EmployeeId = employee.Id,
                WorkstationId = workstation.Id,
                SkillScore = request.SkillScore,
                IsPrimarySkill = isPrimary,
                Status = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.EmployeeSkills.Add(skill);
        }
        else
        {
            skill.SkillScore = request.SkillScore;
            skill.IsPrimarySkill = isPrimary;
            skill.Status = 1;
            skill.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var afterContent = await BuildMatrixContentAsync(employee.Id, cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_SKILL_CELL",
            "EMPLOYEE_SKILL",
            employee.Id,
            beforeContent,
            afterContent,
            $"修改 {employee.Name} 在 {workstation.Name} 的技能分（{request.SkillScore}，主技能={isPrimary}）",
            cancellationToken);

        return new SkillMatrixCell(employee.Id, workstation.Id, request.SkillScore, isPrimary);
    }

    public async Task SaveAsync(
        long employeeId,
        EmployeeSkillSaveRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(x => x.Id == employeeId && x.StoreId == storeId, cancellationToken);

        if (employee is null)
        {
            throw new NotFoundException("员工不存在");
        }

        var workstationIds = request.Skills.Select(x => x.WorkstationId).Distinct().ToList();
        var validWorkstations = await _dbContext.Workstations
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1 && workstationIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var beforeContent = await BuildMatrixContentAsync(employeeId, cancellationToken);

        // P3-14 修复：批量查询现有技能，避免循环内 N+1 查询
        var existingSkills = await _dbContext.EmployeeSkills
            .Where(x => x.EmployeeId == employeeId && workstationIds.Contains(x.WorkstationId))
            .ToDictionaryAsync(x => x.WorkstationId, cancellationToken);

        foreach (var item in request.Skills)
        {
            if (!validWorkstations.Contains(item.WorkstationId))
            {
                throw new BusinessException($"工作站 {item.WorkstationId} 不存在或已停用", "INVALID_WORKSTATION");
            }

            if (item.SkillScore < 0 || item.SkillScore > 5)
            {
                throw new BusinessException("技能分必须在 0 到 5 之间", "INVALID_SKILL_SCORE");
            }

            existingSkills.TryGetValue(item.WorkstationId, out var skill);

            if (skill is null)
            {
                _dbContext.EmployeeSkills.Add(new EmployeeSkillEntity
                {
                    EmployeeId = employeeId,
                    WorkstationId = item.WorkstationId,
                    SkillScore = item.SkillScore,
                    IsPrimarySkill = item.IsPrimarySkill,
                    Status = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                skill.SkillScore = item.SkillScore;
                skill.IsPrimarySkill = item.IsPrimarySkill;
                skill.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var afterContent = await BuildMatrixContentAsync(employeeId, cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "SAVE_EMPLOYEE_SKILLS",
            "EMPLOYEE_SKILL",
            employeeId,
            beforeContent,
            afterContent,
            $"保存员工 {employee.Name} 的技能配置",
            cancellationToken);
    }

    public async Task<SkillMatrixGeneralistResult> SetGeneralistAsync(
        SkillMatrixGeneralistRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(x => x.Id == request.EmployeeId && x.StoreId == storeId && x.Status == 1, cancellationToken)
            ?? throw new NotFoundException("员工不存在");

        if (employee.IsParttime == 1)
        {
            throw new BusinessException("兼职员工不参与评级，无需设置通岗", "INVALID_GENERALIST_EMPLOYEE");
        }

        var enable = request.IsGeneralist == 1;

        // 楼面低技能岗位 = 传送/保洁/咨客/服务等（is_low_skill=1）
        var lowSkillWorkstations = await _dbContext.Workstations
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1 && x.IsLowSkill == 1)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var beforeContent = await BuildMatrixContentAsync(employee.Id, cancellationToken);

        var existingSkills = await _dbContext.EmployeeSkills
            .Where(x => x.EmployeeId == employee.Id && lowSkillWorkstations.Contains(x.WorkstationId))
            .ToDictionaryAsync(x => x.WorkstationId, cancellationToken);

        foreach (var workstationId in lowSkillWorkstations)
        {
            existingSkills.TryGetValue(workstationId, out var skill);
            if (enable)
            {
                // 通岗：至少 3 分（已有更高分保留）
                var score = skill is null ? GeneralistBaseScore : Math.Max(skill.SkillScore, GeneralistBaseScore);
                if (skill is null)
                {
                    _dbContext.EmployeeSkills.Add(new EmployeeSkillEntity
                    {
                        EmployeeId = employee.Id,
                        WorkstationId = workstationId,
                        SkillScore = score,
                        IsPrimarySkill = 0,
                        Status = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    skill.SkillScore = score;
                    skill.Status = 1;
                    skill.UpdatedAt = DateTime.UtcNow;
                }
            }
            else if (skill is not null)
            {
                // 取消通岗：楼面低技能岗位清 0（行保留）
                skill.SkillScore = 0;
                skill.IsPrimarySkill = 0;
                skill.UpdatedAt = DateTime.UtcNow;
            }
        }

        employee.IsGeneralist = enable ? 1 : 0;
        employee.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var afterContent = await BuildMatrixContentAsync(employee.Id, cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "SET_EMPLOYEE_GENERALIST",
            "EMPLOYEE_SKILL",
            employee.Id,
            beforeContent,
            afterContent,
            $"{(enable ? "设置" : "取消")}员工 {employee.Name} 通岗（楼面低技能岗位自动{(enable ? "≥3 分" : "清 0")}）",
            cancellationToken);

        var cells = await _dbContext.EmployeeSkills
            .AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && lowSkillWorkstations.Contains(x.WorkstationId))
            .Select(x => new SkillMatrixCell(x.EmployeeId, x.WorkstationId, x.SkillScore, x.IsPrimarySkill))
            .ToListAsync(cancellationToken);

        return new SkillMatrixGeneralistResult(employee.Id, employee.IsGeneralist, cells);
    }

    private const int GeneralistBaseScore = 3;

    private async Task<string?> BuildMatrixContentAsync(long employeeId, CancellationToken cancellationToken)
    {
        var skills = await _dbContext.EmployeeSkills
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => $"{x.WorkstationId}:{x.SkillScore}:{x.IsPrimarySkill}")
            .ToListAsync(cancellationToken);

        return skills.Count == 0 ? null : string.Join(";", skills);
    }
}
