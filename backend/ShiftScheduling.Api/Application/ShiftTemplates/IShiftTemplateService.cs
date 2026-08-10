using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.ShiftTemplates;

public interface IShiftTemplateService
{
    Task<IReadOnlyList<ShiftTemplateItem>> ListAllAsync(long storeId, CancellationToken cancellationToken);

    Task<ShiftTemplateItem> UpdateAsync(long id, ShiftTemplateUpdateRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
