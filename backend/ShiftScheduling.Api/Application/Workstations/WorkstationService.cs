using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.Workstations;

public sealed class WorkstationService : IWorkstationService
{
    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public WorkstationService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<IReadOnlyList<WorkstationItem>> ListAllAsync(long storeId, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _dbContext.Workstations
            .AsNoTracking()
            .Where(x => x.StoreId == storeId);

        if (!includeInactive)
        {
            query = query.Where(x => x.Status == 1);
        }

        return await query
            .OrderBy(x => x.SortOrder)
            .Select(x => new WorkstationItem(x.Id, x.Code, x.Name, x.SortOrder, x.IsLowSkill, x.Remark, x.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkstationItem> CreateAsync(
        WorkstationCreateRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new BusinessException("工作站编码不能为空", "INVALID_WORKSTATION");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BusinessException("工作站名称不能为空", "INVALID_WORKSTATION");

        var exists = await _dbContext.Workstations
            .AnyAsync(x => x.StoreId == storeId && x.Code == request.Code.Trim(), cancellationToken);
        if (exists)
            throw new BusinessException($"工作站编码 '{request.Code.Trim()}' 已存在", "DUPLICATE_WORKSTATION");

        var entity = new WorkstationEntity
        {
            StoreId = storeId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            IsLowSkill = request.IsLowSkill == 1 ? 1 : 0,
            Remark = request.Remark,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Workstations.Add(entity);

        // 审计与业务数据同一事务提交
        _auditLogService.AddAuditEntity(
            _dbContext, storeId, operatorUserId, operatorName,
            "CREATE_WORKSTATION", "WORKSTATION", null,
            null,
            System.Text.Json.JsonSerializer.Serialize(new { entity.Code, entity.Name, entity.SortOrder, entity.IsLowSkill, entity.Remark }),
            "新增工作站",
            DateTime.UtcNow);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (ExistsCode(storeId, request.Code.Trim()))
        {
            // 并发兜底：查重与保存之间的竞态由数据库唯一索引拦截，转成友好错误
            throw new BusinessException($"工作站编码 '{request.Code.Trim()}' 已存在", "DUPLICATE_WORKSTATION");
        }

        return new WorkstationItem(entity.Id, entity.Code, entity.Name, entity.SortOrder, entity.IsLowSkill, entity.Remark, entity.Status);
    }

    /// <summary>同步查重（用于捕获 DbUpdateException 时的并发兜底判断）。</summary>
    private bool ExistsCode(long storeId, string code)
        => _dbContext.Workstations.Any(x => x.StoreId == storeId && x.Code == code);

    public async Task<WorkstationItem> UpdateAsync(
        long id,
        WorkstationUpdateRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BusinessException("工作站名称不能为空", "INVALID_WORKSTATION");
        }

        var workstation = await _dbContext.Workstations
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken);

        if (workstation is null)
        {
            throw new NotFoundException("工作站不存在");
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { workstation.Name, workstation.Remark, workstation.Status, workstation.IsLowSkill });

        workstation.Name = request.Name.Trim();
        workstation.Remark = request.Remark;
        workstation.Status = request.Status;
        workstation.IsLowSkill = request.IsLowSkill == 1 ? 1 : 0;
        workstation.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_WORKSTATION",
            "WORKSTATION",
            workstation.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { workstation.Name, workstation.Remark, workstation.Status, workstation.IsLowSkill }),
            "编辑工作站",
            cancellationToken);

        return new WorkstationItem(
            workstation.Id,
            workstation.Code,
            workstation.Name,
            workstation.SortOrder,
            workstation.IsLowSkill,
            workstation.Remark,
            workstation.Status);
    }
}