namespace ShiftScheduling.Api.Application.EmployeeSkills;

public interface IEmployeeSkillService
{
    Task<EmployeeSkillMatrix> GetByEmployeeAsync(long employeeId, long storeId, CancellationToken cancellationToken);

    Task SaveAsync(long employeeId, EmployeeSkillSaveRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
