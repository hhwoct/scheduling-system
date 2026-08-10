using ShiftScheduling.Api.Application.Common;

namespace ShiftScheduling.Api.Application.Employees;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeListItem>> QueryAsync(EmployeeQueryRequest request, long storeId, CancellationToken cancellationToken);

    Task<EmployeeDetail> GetByIdAsync(long id, long storeId, CancellationToken cancellationToken);

    Task<EmployeeDetail> CreateAsync(EmployeeUpsertRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    Task<EmployeeDetail> UpdateAsync(long id, EmployeeUpsertRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    Task DeactivateAsync(long id, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
