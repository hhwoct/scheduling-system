namespace ShiftScheduling.Api.Application.EmployeeSkills;

public sealed record EmployeeSkillItem(
    long WorkstationId,
    string WorkstationCode,
    string WorkstationName,
    int SkillScore,
    int IsPrimarySkill);

public sealed record EmployeeSkillMatrix(long EmployeeId, IReadOnlyList<EmployeeSkillItem> Skills);

public sealed record EmployeeSkillSaveItem(long WorkstationId, int SkillScore, int IsPrimarySkill);

public sealed record EmployeeSkillSaveRequest(IReadOnlyList<EmployeeSkillSaveItem> Skills);

// ============ 技能等级总览（矩阵视图） ============

public sealed record SkillMatrixWorkstation(long Id, string Code, string Name);

public sealed record SkillMatrixEmployee(
    long Id,
    string EmployeeNo,
    string Name,
    string Department,
    int IsParttime,
    int IsGeneralist);

public sealed record SkillMatrixCell(
    long EmployeeId,
    long WorkstationId,
    int SkillScore,
    int IsPrimarySkill);

public sealed record SkillMatrixOverview(
    IReadOnlyList<SkillMatrixWorkstation> Workstations,
    IReadOnlyList<SkillMatrixEmployee> Employees,
    IReadOnlyList<SkillMatrixCell> Cells);

/// <summary>单格技能修改请求（技能等级总览页点格子编辑）。</summary>
public sealed record SkillMatrixCellUpdateRequest(
    long EmployeeId,
    long WorkstationId,
    int SkillScore,
    int IsPrimarySkill);

/// <summary>通岗设置请求（1=通岗，0=取消）。</summary>
public sealed record SkillMatrixGeneralistRequest(
    long EmployeeId,
    int IsGeneralist);

/// <summary>通岗设置结果（含受影响的楼面低技能格子）。</summary>
public sealed record SkillMatrixGeneralistResult(
    long EmployeeId,
    int IsGeneralist,
    IReadOnlyList<SkillMatrixCell> Cells);
