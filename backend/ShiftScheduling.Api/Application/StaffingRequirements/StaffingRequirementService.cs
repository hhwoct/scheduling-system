using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.StaffingRequirements;

public sealed class StaffingRequirementService : IStaffingRequirementService
{
    /// <summary>每天半小时时段数：00:00-23:30 共 48 段。</summary>
    private const int SlotCount = 48;

    private const int MaxEntries = 5000;

    private const int MaxRequiredCount = 99;

    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public StaffingRequirementService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<IReadOnlyList<StaffingRequirementItem>> ListAsync(
        long storeId,
        string? dayType,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(dayType) && !StaffingDayTypes.IsValid(dayType))
        {
            throw new BusinessException("日期类型无效，仅支持 WORKDAY/WEEKEND/HOLIDAY", "INVALID_DAY_TYPE");
        }

        var query = _dbContext.StaffingRequirements
            .AsNoTracking()
            .Where(x => x.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(dayType))
        {
            query = query.Where(x => x.DayType == dayType);
        }

        return await query
            .Join(_dbContext.Workstations,
                r => r.WorkstationId,
                w => w.Id,
                (r, w) => new { Requirement = r, Workstation = w })
            .OrderBy(x => x.Workstation.SortOrder)
            .ThenBy(x => x.Workstation.Id)
            .ThenBy(x => x.Requirement.TimeSlot)
            .Select(x => new StaffingRequirementItem(
                x.Requirement.Id,
                x.Requirement.DayType,
                x.Requirement.WorkstationId,
                x.Workstation.Code,
                x.Workstation.Name,
                x.Requirement.TimeSlot,
                x.Requirement.RequiredCount,
                x.Requirement.IdealCount,
                x.Requirement.Remark))
            .ToListAsync(cancellationToken);
    }

    public async Task<StaffingRequirementSaveResult> SaveAsync(
        StaffingRequirementSaveRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (!StaffingDayTypes.IsValid(request.DayType))
        {
            throw new BusinessException("日期类型无效，仅支持 WORKDAY/WEEKEND/HOLIDAY", "INVALID_DAY_TYPE");
        }

        var entries = request.Entries;
        if (entries is null || entries.Count == 0)
        {
            throw new BusinessException("请至少提交一条人数需求", "EMPTY_STAFFING_REQUIREMENT");
        }

        if (entries.Count > MaxEntries)
        {
            throw new BusinessException($"单次最多提交 {MaxEntries} 条人数需求", "TOO_MANY_ENTRIES");
        }

        // 校验工作站均属于当前门店
        var submittedWorkstationIds = entries.Select(e => e.WorkstationId).Distinct().ToList();
        var validWorkstationIds = await _dbContext.Workstations
            .Where(w => w.StoreId == storeId && submittedWorkstationIds.Contains(w.Id))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
        var invalidWorkstationIds = submittedWorkstationIds.Except(validWorkstationIds).ToList();
        if (invalidWorkstationIds.Count > 0)
        {
            throw new BusinessException($"工作站不存在：{string.Join(", ", invalidWorkstationIds)}", "INVALID_WORKSTATION");
        }

        // 解析并校验时段与两档人数（最少 <= 最好，30 分钟对齐、00:00-23:30、不重复）
        var parsed = new Dictionary<(long WorkstationId, TimeSpan Slot), (int Min, int Ideal, string? Remark)>();
        foreach (var entry in entries)
        {
            if (entry.RequiredCount is < 0 or > MaxRequiredCount)
            {
                throw new BusinessException(
                    $"最少人数必须在 0-{MaxRequiredCount} 之间（工作站 {entry.WorkstationId} 时段 {entry.TimeSlot}）",
                    "INVALID_REQUIRED_COUNT");
            }

            var ideal = entry.IdealCount <= 0 ? entry.RequiredCount : entry.IdealCount;
            if (ideal > MaxRequiredCount)
            {
                throw new BusinessException(
                    $"最好人数必须在 0-{MaxRequiredCount} 之间（工作站 {entry.WorkstationId} 时段 {entry.TimeSlot}）",
                    "INVALID_IDEAL_COUNT");
            }

            // 最好人数不得低于最少人数，自动抬升
            if (ideal < entry.RequiredCount)
            {
                ideal = entry.RequiredCount;
            }

            var remark = string.IsNullOrWhiteSpace(entry.Remark) ? null : entry.Remark.Trim();
            if (remark is { Length: > 200 })
            {
                throw new BusinessException(
                    $"备注最长 200 字符（工作站 {entry.WorkstationId} 时段 {entry.TimeSlot}）",
                    "INVALID_REMARK");
            }

            if (!TryParseSlot(entry.TimeSlot, out var slot))
            {
                throw new BusinessException(
                    $"时段格式不正确：{entry.TimeSlot}（需 HH:mm 且 30 分钟对齐、在 00:00-23:30 内）",
                    "INVALID_TIME_SLOT");
            }

            if (!parsed.TryAdd((entry.WorkstationId, slot), (entry.RequiredCount, ideal, remark)))
            {
                throw new BusinessException(
                    $"时段重复提交：工作站 {entry.WorkstationId} 时段 {entry.TimeSlot}",
                    "DUPLICATE_TIME_SLOT");
            }
        }

        // 全矩阵 upsert：整店工作站 × 48 个半小时时段
        var allWorkstationIds = await _dbContext.Workstations
            .Where(w => w.StoreId == storeId)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var existing = await _dbContext.StaffingRequirements
            .Where(x => x.StoreId == storeId && x.DayType == request.DayType)
            .ToDictionaryAsync(x => (x.WorkstationId, x.TimeSlot), x => x, cancellationToken);

        var beforeNonZero = existing.Values.Count(x => x.RequiredCount > 0 || x.IdealCount > 0);
        var now = DateTime.UtcNow;
        var changed = 0;
        var nonZero = 0;

        foreach (var workstationId in allWorkstationIds)
        {
            for (var i = 0; i < SlotCount; i++)
            {
                var slot = TimeSpan.FromMinutes(i * 30);
                var counts = parsed.GetValueOrDefault((workstationId, slot));
                if (counts.Min > 0 || counts.Ideal > 0)
                {
                    nonZero++;
                }

                if (existing.TryGetValue((workstationId, slot), out var entity))
                {
                    if (entity.RequiredCount != counts.Min || entity.IdealCount != counts.Ideal || entity.Remark != counts.Remark)
                    {
                        entity.RequiredCount = counts.Min;
                        entity.IdealCount = counts.Ideal;
                        entity.Remark = counts.Remark;
                        entity.UpdatedAt = now;
                        changed++;
                    }
                }
                else
                {
                    _dbContext.StaffingRequirements.Add(new StaffingRequirementEntity
                    {
                        StoreId = storeId,
                        DayType = request.DayType,
                        WorkstationId = workstationId,
                        TimeSlot = slot,
                        RequiredCount = counts.Min,
                        IdealCount = counts.Ideal,
                        Remark = counts.Remark,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    changed++;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var total = allWorkstationIds.Count * SlotCount;
        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "SAVE_STAFFING_REQUIREMENTS",
            "STAFFING_REQUIREMENT",
            0,
            $"{request.DayType}: 原非零时段 {beforeNonZero} 条",
            $"{request.DayType}: 非零时段 {nonZero} 条 / 共 {total} 格",
            $"保存{request.DayType}人数需求配置（{changed} 格变更）",
            cancellationToken);

        return new StaffingRequirementSaveResult(request.DayType, total, nonZero);
    }

    private static bool TryParseSlot(string? value, out TimeSpan slot)
    {
        slot = default;
        if (string.IsNullOrWhiteSpace(value) || !TimeSpan.TryParse(value, out var parsed))
        {
            return false;
        }

        if (parsed < TimeSpan.Zero || parsed >= TimeSpan.FromHours(24))
        {
            return false;
        }

        // 与算法时段粒度一致：30 分钟对齐
        if (parsed.Minutes % 30 != 0 || parsed.Seconds != 0)
        {
            return false;
        }

        slot = TimeSpan.FromMinutes(parsed.Hours * 60 + parsed.Minutes);
        return true;
    }
}
