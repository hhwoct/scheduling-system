using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;

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
                x.Version))
            .ToListAsync(cancellationToken);
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

        var rule = await _dbContext.RuleConfigs
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken);

        if (rule is null)
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

        // 乐观锁：客户端提供版本时执行 compare-and-swap，避免两个管理员同时保存互相覆盖
        if (request.Version is not null && rule.Version != request.Version)
        {
            throw new BusinessException("规则已被他人修改，请刷新后重试", "RULE_VERSION_CONFLICT");
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { rule.RuleValue, rule.Status });

        rule.RuleValue = request.RuleValue.Trim();
        rule.Status = request.Status == 0 ? 0 : 1;
        rule.Version++;
        rule.UpdatedAt = DateTime.UtcNow;

        // 同步「最大周工时」：修改全局默认值时，跟随默认的员工个人周工时上限一并更新。
        var syncedEmployeeCount = 0;
        var syncedMaxWeeklyHours = 0m;
        if (rule.RuleKey == "max_weekly_hours"
            && decimal.TryParse(rule.RuleValue, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out syncedMaxWeeklyHours)
            && syncedMaxWeeklyHours > 0)
        {
            var followDefaultEmployees = await _dbContext.Employees
                .Where(x => x.StoreId == storeId && x.WeeklyHoursFollowDefault == 1)
                .ToListAsync(cancellationToken);

            foreach (var employee in followDefaultEmployees)
            {
                employee.MaxWeeklyHours = syncedMaxWeeklyHours;
                employee.UpdatedAt = DateTime.UtcNow;
            }

            syncedEmployeeCount = followDefaultEmployees.Count;
        }

        // 修复：审计与业务数据在同一事务内提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_RULE_CONFIG",
            "RULE_CONFIG",
            rule.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { rule.RuleValue, rule.Status }),
            $"修改规则 {rule.RuleName}",
            DateTime.UtcNow);

        if (syncedEmployeeCount > 0)
        {
            _auditLogService.AddAuditEntity(
                _dbContext,
                storeId,
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
            // Version 已配置为并发令牌：UPDATE 带 WHERE version=旧值，并发冲突抛 DbUpdateConcurrencyException
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
            rule.Version);
    }
}
