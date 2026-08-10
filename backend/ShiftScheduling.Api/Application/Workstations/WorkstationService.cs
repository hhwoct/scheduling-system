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
            .Select(x => new WorkstationItem(x.Id, x.Code, x.Name, x.SortOrder, x.Remark, x.Status))
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
            Remark = request.Remark,
            Status = 1,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _dbContext.Workstations.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId, operatorUserId, operatorName,
            "CREATE_WORKSTATION", "WORKSTATION", entity.Id,
            null,
            System.Text.Json.JsonSerializer.Serialize(new { entity.Code, entity.Name, entity.SortOrder, entity.Remark }),
            "新增工作站", cancellationToken);

        return new WorkstationItem(entity.Id, entity.Code, entity.Name, entity.SortOrder, entity.Remark, entity.Status);
    }

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

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { workstation.Name, workstation.Remark, workstation.Status });

        workstation.Name = request.Name.Trim();
        workstation.Remark = request.Remark;
        workstation.Status = request.Status;
        workstation.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_WORKSTATION",
            "WORKSTATION",
            workstation.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { workstation.Name, workstation.Remark, workstation.Status }),
            "编辑工作站",
            cancellationToken);

        return new WorkstationItem(
            workstation.Id,
            workstation.Code,
            workstation.Name,
            workstation.SortOrder,
            workstation.Remark,
            workstation.Status);
    }
}
