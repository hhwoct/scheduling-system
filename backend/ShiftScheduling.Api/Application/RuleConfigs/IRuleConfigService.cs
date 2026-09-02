using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.RuleConfigs;

public interface IRuleConfigService
{
    /// <summary>列出某范围的原始规则行(storeId=0 为全局默认)。</summary>
    Task<IReadOnlyList<RuleConfigItem>> ListAllAsync(long storeId, CancellationToken cancellationToken);

    /// <summary>列出门店生效规则:全局默认 + 本店覆盖(同 key 门店行优先),Source 标记来源。</summary>
    Task<IReadOnlyList<RuleConfigItem>> ListEffectiveAsync(long storeId, CancellationToken cancellationToken);

    /// <summary>
    /// 保存规则:
    ///  - admin(storeId=0):修改全局默认行
    ///  - 店长(storeId=门店):修改本店行;若修改的是继承自全局的行,自动生成本店覆盖行
    /// </summary>
    Task<RuleConfigItem> UpdateAsync(
        long id,
        RuleConfigUpdateRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>删除本店覆盖行(恢复继承全局默认),仅门店行可删。</summary>
    Task DeleteAsync(long id, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
