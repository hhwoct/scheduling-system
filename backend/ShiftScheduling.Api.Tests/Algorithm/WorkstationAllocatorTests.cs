using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Algorithm.Steps;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>工作站分配器单元测试。</summary>
public sealed class WorkstationAllocatorTests
{
    private static readonly DateOnly Start = new(2026, 8, 3); // 周一

    private static SchedulingInput BuildInput(
        List<EmployeeInput> employees,
        List<SkillInput> skills,
        ShiftTemplateInput shift,
        List<StaffingRequirementInput> requirements)
        => AlgorithmTestData.Build(
            employees,
            skills,
            AlgorithmTestData.Days(Start, 1),
            new List<ShiftTemplateInput> { shift },
            requirements);

    private static List<ShiftAssignment> ShiftsFor(params long[] employeeIds)
        => employeeIds.Select((id, i) => new ShiftAssignment(id, Start, 1, "S4")).ToList();

    [Fact]
    public void Allocate_AssignsMainWorkstationBySkill()
    {
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10, 11);
        var input = BuildInput(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E001", "甲", "楼面") },
            new List<SkillInput> { AlgorithmTestData.Skill(1, 10, 5), AlgorithmTestData.Skill(1, 11, 3) },
            shift,
            AlgorithmTestData.ReqsForShift("WORKDAY", shift, 1, 10));

        var issues = new List<ScheduleIssueOutput>();
        var result = new WorkstationAllocator().Allocate(
            input,
            new List<RestDayAssignment>(),
            ShiftsFor(1),
            issues);

        // 甲技能最高的是工作站 10 → 全部分配到 10
        Assert.NotEmpty(result);
        Assert.All(result, a => Assert.Equal(10, a.WorkstationId));
        Assert.Equal(5, result[0].SkillScore);
    }

    [Fact]
    public void Allocate_ReportsStaffingGapWhenCoverageBelowDemand()
    {
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10);
        var input = BuildInput(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E001", "甲", "楼面") },
            new List<SkillInput> { AlgorithmTestData.Skill(1, 10) },
            shift,
            AlgorithmTestData.ReqsForShift("WORKDAY", shift, 2, 10)); // 需求 2 人，只有 1 人

        var issues = new List<ScheduleIssueOutput>();
        var result = new WorkstationAllocator().Allocate(
            input,
            new List<RestDayAssignment>(),
            ShiftsFor(1),
            issues);

        Assert.Contains(issues, x => x.IssueType == "STAFFING_GAP" && x.Severity == "WARN");
        Assert.Contains(issues, x => x.Description.Contains("缺 1 人"));
    }

    [Fact]
    public void Allocate_NoSkillEmployee_ReportsUnassignedStaff()
    {
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10);
        var input = BuildInput(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E001", "甲", "楼面") },
            new List<SkillInput>(), // 无技能
            shift,
            AlgorithmTestData.ReqsForShift("WORKDAY", shift, 1, 10));

        var issues = new List<ScheduleIssueOutput>();
        var result = new WorkstationAllocator().Allocate(
            input,
            new List<RestDayAssignment>(),
            ShiftsFor(1),
            issues);

        // 无技能员工不产生工作站分配，产生 UNASSIGNED_STAFF 告警
        Assert.Empty(result);
        Assert.Contains(issues, x => x.IssueType == "UNASSIGNED_STAFF");
    }

    [Fact]
    public void Allocate_FallbackWorkstation_PicksHighestSkillCoveredStation()
    {
        // 员工只掌握 10 号站技能（不在需求中），需求只在低技能 99 号站：
        // 无参考分配时兜底选择班次覆盖范围内技能最高的岗位（10），并报告 99 号站缺口
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10, 99);
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E001", "甲", "楼面") },
            new List<SkillInput> { AlgorithmTestData.Skill(1, 10, 3) },
            AlgorithmTestData.Days(Start, 1),
            new List<ShiftTemplateInput> { shift },
            AlgorithmTestData.ReqsForShift("WORKDAY", shift, 1, 99));
        input = input with { LowSkillWorkstationIds = new Dictionary<long, bool> { [99] = true } };

        var issues = new List<ScheduleIssueOutput>();
        var result = new WorkstationAllocator().Allocate(
            input,
            new List<RestDayAssignment>(),
            ShiftsFor(1),
            issues);

        // 兜底分配到技能最高的覆盖岗位 10
        Assert.NotEmpty(result);
        Assert.All(result, a => Assert.Equal(10, a.WorkstationId));
        // 99 号站需求无人覆盖 → STAFFING_GAP
        Assert.Contains(issues, x => x.IssueType == "STAFFING_GAP" && x.WorkstationId == 99);
    }

    [Fact]
    public void Allocate_AssignsMainWorkstationToEachShiftEmployee()
    {
        // 工作站 10 需求 2 人：员工甲（技能10）、乙（技能10+11）
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10, 11);
        var requirements = AlgorithmTestData.ReqsForShift("WORKDAY", shift, 2, 10);
        requirements.AddRange(AlgorithmTestData.ReqsForShift("WORKDAY", shift, 1, 11));

        var input = BuildInput(
            new List<EmployeeInput>
            {
                AlgorithmTestData.Employee(1, "E001", "甲", "楼面"),
                AlgorithmTestData.Employee(2, "E002", "乙", "楼面")
            },
            new List<SkillInput>
            {
                AlgorithmTestData.Skill(1, 10, 5),
                AlgorithmTestData.Skill(2, 10, 4),
                AlgorithmTestData.Skill(2, 11, 3)
            },
            shift,
            requirements);

        var issues = new List<ScheduleIssueOutput>();
        var result = new WorkstationAllocator().Allocate(
            input,
            new List<RestDayAssignment>(),
            ShiftsFor(1, 2),
            issues);

        // 2 名员工 × 6 个半小时时段 = 12 条分配记录
        Assert.Equal(12, result.Count);
        // 主工作站按技能分：两人技能最高都是 10 → 全部落在 10 号站
        Assert.All(result, a => Assert.Equal(10, a.WorkstationId));
        Assert.All(result, a => Assert.True(a.SkillScore > 0));
    }
}