using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.ShiftTemplates;

public sealed class ShiftTemplateService : IShiftTemplateService
{
    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public ShiftTemplateService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<IReadOnlyList<ShiftTemplateItem>> ListAllAsync(long storeId, CancellationToken cancellationToken)
    {
        var templates = await _dbContext.ShiftTemplates
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        var templateIds = templates.Select(x => x.Id).ToList();
        var workstationNames = await _dbContext.ShiftWorkstations
            .AsNoTracking()
            .Where(x => templateIds.Contains(x.ShiftTemplateId))
            .Join(
                _dbContext.Workstations.AsNoTracking(),
                sw => sw.WorkstationId,
                w => w.Id,
                (sw, w) => new { sw.ShiftTemplateId, w.Name })
            .ToListAsync(cancellationToken);

        var lookup = workstationNames
            .GroupBy(x => x.ShiftTemplateId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).OrderBy(n => n).ToList());

        return templates
            .Select(t => new ShiftTemplateItem(
                t.Id,
                t.Code,
                t.Name,
                t.StartTime,
                t.EndTime,
                t.IsCrossDay,
                t.Priority,
                t.Status,
                lookup.TryGetValue(t.Id, out var names) ? names : Array.Empty<string>()))
            .ToList();
    }

    public async Task<ShiftTemplateItem> UpdateAsync(
        long id,
        ShiftTemplateUpdateRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BusinessException("班次名称不能为空", "INVALID_SHIFT_TEMPLATE");
        }

        if (request.StartTime == request.EndTime)
        {
            throw new BusinessException("开始时间和结束时间不能相同", "INVALID_SHIFT_TEMPLATE");
        }

        var template = await _dbContext.ShiftTemplates
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken);

        if (template is null)
        {
            throw new NotFoundException("班次不存在");
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { template.Name, template.StartTime, template.EndTime, template.IsCrossDay, template.Priority, template.Status });

        template.Name = request.Name.Trim();
        template.StartTime = request.StartTime;
        template.EndTime = request.EndTime;
        template.IsCrossDay = request.IsCrossDay;
        template.Priority = request.Priority;
        template.Status = request.Status;
        template.UpdatedAt = DateTime.UtcNow;

        // 修复：审计与业务数据在同一事务内提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_SHIFT_TEMPLATE",
            "SHIFT_TEMPLATE",
            template.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { template.Name, template.StartTime, template.EndTime, template.IsCrossDay, template.Priority, template.Status }),
            "编辑班次",
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var workstationNames = await _dbContext.ShiftWorkstations
            .AsNoTracking()
            .Where(x => x.ShiftTemplateId == template.Id)
            .Join(
                _dbContext.Workstations.AsNoTracking(),
                sw => sw.WorkstationId,
                w => w.Id,
                (sw, w) => w.Name)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        return new ShiftTemplateItem(
            template.Id,
            template.Code,
            template.Name,
            template.StartTime,
            template.EndTime,
            template.IsCrossDay,
            template.Priority,
            template.Status,
            workstationNames);
    }
}
