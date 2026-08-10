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
                x.Status))
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
            if (!decimal.TryParse(request.RuleValue, out var number) || number < 0)
            {
                throw new BusinessException("数字类型规则值必须为非负数", "INVALID_RULE_VALUE");
            }
        }

        var beforeContent = System.Text.Json.JsonSerializer.Serialize(new { rule.RuleValue, rule.Status });

        rule.RuleValue = request.RuleValue.Trim();
        rule.Status = request.Status;
        rule.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "UPDATE_RULE_CONFIG",
            "RULE_CONFIG",
            rule.Id,
            beforeContent,
            System.Text.Json.JsonSerializer.Serialize(new { rule.RuleValue, rule.Status }),
            $"修改规则 {rule.RuleName}",
            cancellationToken);

        return new RuleConfigItem(
            rule.Id,
            rule.RuleKey,
            rule.RuleName,
            rule.RuleValue,
            rule.ValueType,
            rule.Remark,
            rule.Status);
    }
}
