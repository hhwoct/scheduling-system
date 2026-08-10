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
