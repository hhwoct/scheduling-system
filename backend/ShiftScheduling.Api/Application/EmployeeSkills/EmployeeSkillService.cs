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
