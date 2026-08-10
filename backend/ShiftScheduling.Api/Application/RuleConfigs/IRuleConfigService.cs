using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.RuleConfigs;

public interface IRuleConfigService
{
    Task<IReadOnlyList<RuleConfigItem>> ListAllAsync(long storeId, CancellationToken cancellationToken);

    Task<RuleConfigItem> UpdateAsync(long id, RuleConfigUpdateRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
