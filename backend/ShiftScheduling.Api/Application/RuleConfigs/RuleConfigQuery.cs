using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Application.RuleConfigs;

/// <summary>
/// 规则生效口径:store_id=0 为 admin 维护的全局默认,门店行(store_id=门店)
/// 为店长在全局基础上的覆盖。生效值 = 全局 + 本店覆盖(同 key 时本店行优先)。
/// 排班引擎、偏好学习、员工默认周工时等所有规则消费者统一走这里。
/// </summary>
public static class RuleConfigQuery
{
    public const long GlobalStoreId = 0;

    public static async Task<Dictionary<string, string>> GetEffectiveAsync(
        ShiftSchedulingDbContext dbContext,
        long storeId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.RuleConfigs
            .AsNoTracking()
            .Where(x => x.Status == 1 && (x.StoreId == GlobalStoreId || x.StoreId == storeId))
            .Select(x => new { x.StoreId, x.RuleKey, x.RuleValue })
            .ToListAsync(cancellationToken);

        // 同 key 时 storeId 大的(门店行)覆盖全局行
        return rows
            .GroupBy(x => x.RuleKey)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.StoreId).Last().RuleValue);
    }
}
