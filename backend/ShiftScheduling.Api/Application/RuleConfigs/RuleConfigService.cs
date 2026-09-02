using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.RuleConfigs;

public sealed class RuleConfigService : IRuleConfigService
{
    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public RuleConfigService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<IReadOnlyList<RuleConfigItem>> ListAllAsync(long storeId, CancellationToken cancellationToken)
    {
        return await _dbContext.RuleConfigs
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.Id)
            .Select(x => new RuleConfigItem(
                x.Id,
                x.RuleKey,
                x.RuleName,
                x.RuleValue,
                x.ValueType,
                x.Remark,
                x.Status,
                x.Version,
                storeId == RuleConfigQuery.GlobalStoreId ? "GLOBAL" : "STORE"))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleConfigItem>> ListEffectiveAsync(long storeId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.RuleConfigs
            .AsNoTracking()
            .Where(x => x.StoreId == RuleConfigQuery.GlobalStoreId || x.StoreId == storeId)
            .OrderBy(x => x.StoreId)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var merged = new List<RuleConfigItem>();
        foreach (var group in rows.GroupBy(x => x.RuleKey))
        {
            var storeRow = group.FirstOrDefault(x => x.StoreId == storeId);
            var src = storeRow ?? group.First();
            merged.Add(new RuleConfigItem(
                src.Id,
                src.RuleKey,
                src.RuleName,
                src.RuleValue,
                src.ValueType,
                src.Remark,
                src.Status,
                src.Version,
                storeRow is null ? "GLOBAL" : "STORE"));
        }

        return merged.OrderBy(x => x.Id).ToList();
    }

    public async Task<RuleConfigItem> UpdateAsync(
        long id,
        RuleConfigUpdateRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RuleValue))
        {
            throw new BusinessException("规则值不能为空", "INVALID_RULE_VALUE");
        }

        var existing = await _dbContext.RuleConfigs
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("规则不存在");

        // 作用域校验:admin 改全局行;店长改本店行,或首次修改继承行时生成本店覆盖行
        RuleConfigEntity rule;
        var isOverrideCreation = false;
        if (existing.StoreId == storeId)
        {
            rule = existing;
        }
        else if (existing.StoreId == RuleConfigQuery.GlobalStoreId && storeId != RuleConfigQuery.GlobalStoreId)
        {
            // 店长首次修改继承自全局的规则:复制全局行为本店覆盖行
            rule = new RuleConfigEntity
            {
                StoreId = storeId,
                RuleKey = existing.RuleKey,
                RuleName = existing.RuleName,
                RuleValue = existing.RuleValue,
                ValueType = existing.ValueType,
                Remark = existing.Remark,
                Status = existing.Status,
                Version = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.RuleConfigs.Add(rule);
            isOverrideCreation = true;
        }
        else
        {
            throw new NotFoundException("规则不存在");
        }

        if (rule.ValueType == "number")
        {
            if (!decimal.TryParse(request.RuleValue, System.Globalization.CultureInfo.InvariantCulture, out var number) || number < 0)
            {
                throw new BusinessException("数字类型规则值必须为非负数", "INVALID_RULE_VALUE");
            }
        }

        // 乐观锁:修改既有行时校验版本;新建覆盖行跳过
        if (!isOverrideCreation && request.Version is not null && rule.Version != request.Version)
        {
            throw new BusinessException("规则已被他人修改，请刷新后重试", "RULE_VERSION_CONFLICT");
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { rule.RuleValue, rule.Status, rule.StoreId });

        rule.RuleValue = request.RuleValue.Trim();
        rule.Status = request.Status == 0 ? 0 : 1;
        rule.Version++;
        rule.UpdatedAt = DateTime.UtcNow;

        // 同步「最大周工时」:
        //  - 全局行修改:同步所有没有本店覆盖的门店的跟随默认员工
        //  - 本店行修改:同步本店跟随默认员工
        var syncedEmployeeCount = 0;
        var syncedMaxWeeklyHours = 0m;
        if (rule.RuleKey == "max_weekly_hours"
            && decimal.TryParse(rule.RuleValue, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out syncedMaxWeeklyHours)
            && syncedMaxWeeklyHours > 0)
        {
            IQueryable<EmployeeEntity> employeeQuery = _dbContext.Employees.Where(x => x.WeeklyHoursFollowDefault == 1);
            if (rule.StoreId == RuleConfigQuery.GlobalStoreId)
            {
                var storesWithOverride = await _dbContext.RuleConfigs
                    .Where(x => x.RuleKey == "max_weekly_hours" && x.StoreId != RuleConfigQuery.GlobalStoreId)
                    .Select(x => x.StoreId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                employeeQuery = employeeQuery.Where(x => !storesWithOverride.Contains(x.StoreId));
            }
            else
            {
                employeeQuery = employeeQuery.Where(x => x.StoreId == rule.StoreId);
            }

            var followDefaultEmployees = await employeeQuery.ToListAsync(cancellationToken);
            foreach (var employee in followDefaultEmployees)
            {
                employee.MaxWeeklyHours = syncedMaxWeeklyHours;
                employee.UpdatedAt = DateTime.UtcNow;
            }
            syncedEmployeeCount = followDefaultEmployees.Count;
        }

        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId == RuleConfigQuery.GlobalStoreId ? 0 : storeId,
            operatorUserId,
            operatorName,
            "UPDATE_RULE_CONFIG",
            "RULE_CONFIG",
            rule.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { rule.RuleValue, rule.Status, rule.StoreId }),
            (isOverrideCreation ? "门店覆盖全局规则 " : "修改规则 ") + rule.RuleName,
            DateTime.UtcNow);

        if (syncedEmployeeCount > 0)
        {
            _auditLogService.AddAuditEntity(
                _dbContext,
                rule.StoreId == RuleConfigQuery.GlobalStoreId ? 0 : rule.StoreId,
                operatorUserId,
                operatorName,
                "SYNC_EMPLOYEE_WEEKLY_HOURS",
                "EMPLOYEE",
                null,
                null,
                System.Text.Json.JsonSerializer.Serialize(new { maxWeeklyHours = syncedMaxWeeklyHours }),
                $"同步 {syncedEmployeeCount} 名跟随默认员工的周工时上限",
                DateTime.UtcNow);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("规则已被他人修改，请刷新后重试", "RULE_VERSION_CONFLICT");
        }

        return new RuleConfigItem(
            rule.Id,
            rule.RuleKey,
            rule.RuleName,
            rule.RuleValue,
            rule.ValueType,
            rule.Remark,
            rule.Status,
            rule.Version,
            rule.StoreId == RuleConfigQuery.GlobalStoreId ? "GLOBAL" : "STORE");
    }

    public async Task DeleteAsync(
        long id,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (storeId == RuleConfigQuery.GlobalStoreId)
        {
            throw new BusinessException("全局默认规则不可删除", "FORBIDDEN");
        }

        var rule = await _dbContext.RuleConfigs
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("规则不存在");

        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "DELETE_RULE_CONFIG",
            "RULE_CONFIG",
            rule.Id,
            System.Text.Json.JsonSerializer.Serialize(new { rule.RuleValue, rule.Status }),
            null,
            $"恢复全局默认规则 {rule.RuleName}",
            DateTime.UtcNow);

        _dbContext.RuleConfigs.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
