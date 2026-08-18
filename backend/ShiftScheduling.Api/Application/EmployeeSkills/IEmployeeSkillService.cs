namespace ShiftScheduling.Api.Application.EmployeeSkills;

public interface IEmployeeSkillService
{
    Task<EmployeeSkillMatrix> GetByEmployeeAsync(long employeeId, long storeId, CancellationToken cancellationToken);

    Task SaveAsync(long employeeId, EmployeeSkillSaveRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    /// <summary>门店技能等级总览（员工 × 工作站矩阵）。</summary>
    Task<SkillMatrixOverview> GetStoreMatrixAsync(long storeId, CancellationToken cancellationToken);

    /// <summary>修改单个技能格（管理员/店长均可操作）。</summary>
    Task<SkillMatrixCell> UpdateCellAsync(SkillMatrixCellUpdateRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    /// <summary>设置员工通岗（自动为楼面低技能岗位写入/清空基线技能）。</summary>
    Task<SkillMatrixGeneralistResult> SetGeneralistAsync(SkillMatrixGeneralistRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
