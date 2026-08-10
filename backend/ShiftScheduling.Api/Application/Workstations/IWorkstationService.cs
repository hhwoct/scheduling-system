using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.Workstations;

public interface IWorkstationService
{
    Task<IReadOnlyList<WorkstationItem>> ListAllAsync(long storeId, bool includeInactive, CancellationToken cancellationToken);

    Task<WorkstationItem> CreateAsync(WorkstationCreateRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    Task<WorkstationItem> UpdateAsync(long id, WorkstationUpdateRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
