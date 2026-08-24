using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Algorithm.Steps;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>
/// 剩余缺口兜底填充（ResidualGapFiller）单元测试。
/// 重点覆盖审查修复（B）：块与员工已有班次部分重叠时，
/// 应裁剪重叠部分、补不重叠子段，而不是整块拒绝。
/// </summary>
public sealed class ResidualGapFillerTests
{
    private static readonly DateOnly TestDate = new(2026, 9, 1);

    private static List<StaffingRequirementInput> ReqSlots(TimeSpan start, TimeSpan endExclusive, long wsId, int count = 1)
    {
        var list = new List<StaffingRequirementInput>();
        for (var t = start; t < endExclusive; t = t.Add(TimeSpan.FromMinutes(30)))
        {
            list.Add(new StaffingRequirementInput("WORKDAY", wsId, t, count));
        }
        return list;
    }

    [Fact]
    public void Fill_BlockPartiallyOverlapsExistingShift_FillsNonOverlappingSegments()
    {
        // 场景：员工已有 10:00-11:00 班次；需求 08:00-12:00 = 1（连续块 4h）。
        // 修复前：块与 10:00-11:00 重叠 → 整块拒绝，缺口 8 槽全部保留。
        // 修复后：应补 08:00-09:30 与 11:00-11:30 两个可接子段，仅 10:00-10:30 保留缺口。
        const long wsId = 10;
        var emp = AlgorithmTestData.Employee(1, "E001", "甲", "楼面");
        var skill = AlgorithmTestData.Skill(1, wsId);
        var dates = new List<DateParameterInput> { AlgorithmTestData.Day(TestDate) };
        var reqs = ReqSlots(new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), wsId);
        // 已有班次 10:00-11:00：模板必须进 input.ShiftTemplates（Fill 用其构建 shiftById/busySlots）
        var existingShift = new ShiftTemplateInput(1, "SX", "已有班", new TimeSpan(10, 0, 0), new TimeSpan(11, 0, 0), 0, 1, new List<long> { wsId });
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { emp },
            new List<SkillInput> { skill },
            dates,
            new List<ShiftTemplateInput> { existingShift },
            reqs,
            minDailyWorkHours: 0m);

        var shifts = new List<ShiftAssignment> { new(1, TestDate, existingShift.Id, "SX", wsId) };

        var workstationAssignments = new List<WorkstationAssignment>();
        var residualTemplates = new List<ShiftTemplateInput>();

        var residual = ResidualGapFiller.Fill(
            input,
            new List<RestDayAssignment>(),
            shifts,
            workstationAssignments,
            residualTemplates);

        // 08:00-09:30 与 11:00-11:30 的缺口被补掉
        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(8, 0, 0))));
        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(8, 30, 0))));
        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(9, 0, 0))));
        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(9, 30, 0))));
        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(11, 0, 0))));
        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(11, 30, 0))));

        // 10:00-10:30 与已有班次重叠，无人可补，缺口保留
        Assert.Equal(1, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(10, 0, 0))));
        Assert.Equal(1, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(10, 30, 0))));

        // 员工的工作站分配槽位 = 可接子段（不含重叠槽）
        var assignedSlots = workstationAssignments
            .Where(a => a.EmployeeId == 1)
            .Select(a => a.TimeSlot)
            .OrderBy(s => s)
            .ToList();
        Assert.Equal(new[]
        {
            new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0),
            new TimeSpan(9, 0, 0), new TimeSpan(9, 30, 0),
            new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0)
        }, assignedSlots);
    }

    [Fact]
    public void Fill_BlockFullyOverlapsExistingShift_LeavesGap()
    {
        // 场景：员工已有 08:00-12:00 班次；需求同为 08:00-12:00 → 无可接槽，缺口保留。
        const long wsId = 11;
        var emp = AlgorithmTestData.Employee(1, "E001", "甲", "楼面");
        var skill = AlgorithmTestData.Skill(1, wsId);
        var dates = new List<DateParameterInput> { AlgorithmTestData.Day(TestDate) };
        var reqs = ReqSlots(new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), wsId);
        var existingShift = new ShiftTemplateInput(1, "SX", "已有班", new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), 0, 1, new List<long> { wsId });
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { emp },
            new List<SkillInput> { skill },
            dates,
            new List<ShiftTemplateInput> { existingShift },
            reqs,
            minDailyWorkHours: 0m);

        var shifts = new List<ShiftAssignment> { new(1, TestDate, existingShift.Id, "SX", wsId) };

        var workstationAssignments = new List<WorkstationAssignment>();
        var residualTemplates = new List<ShiftTemplateInput>();

        var residual = ResidualGapFiller.Fill(
            input,
            new List<RestDayAssignment>(),
            shifts,
            workstationAssignments,
            residualTemplates);

        // 全部缺口保留
        Assert.Equal(1, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(8, 0, 0))));
        Assert.Equal(1, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(11, 30, 0))));
        Assert.Empty(workstationAssignments.Where(a => a.EmployeeId == 1));
    }

    [Fact]
    public void Fill_NoExistingShift_FillsWholeBlock()
    {
        // 场景：无已有班次 → 整块 08:00-12:00 全部补掉（回归验证原行为不退化）。
        const long wsId = 12;
        var emp = AlgorithmTestData.Employee(1, "E001", "甲", "楼面");
        var skill = AlgorithmTestData.Skill(1, wsId);
        var dates = new List<DateParameterInput> { AlgorithmTestData.Day(TestDate) };
        var reqs = ReqSlots(new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), wsId);
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { emp },
            new List<SkillInput> { skill },
            dates,
            new List<ShiftTemplateInput>(),
            reqs,
            minDailyWorkHours: 0m);

        var workstationAssignments = new List<WorkstationAssignment>();
        var residualTemplates = new List<ShiftTemplateInput>();

        var residual = ResidualGapFiller.Fill(
            input,
            new List<RestDayAssignment>(),
            new List<ShiftAssignment>(),
            workstationAssignments,
            residualTemplates);

        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(8, 0, 0))));
        Assert.Equal(0, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(11, 30, 0))));
        Assert.Equal(8, workstationAssignments.Count(a => a.EmployeeId == 1));
    }

    [Fact]
    public void Fill_FragmentBlock_NoFullTimer_LeavesGap()
    {
        // 权衡回归（B2 回退）：碎片块（< 每日最低工时 6.5h）全职不接（只留兼职）。
        // 曾放开「已上班全职可追加」豁免（缺口 27→9），但副手顶班日单日工时 12h→15h，
        // 与「避免超长工时」的业务诉求冲突，故保留外层 if：碎片块缺口如实报告（WARN 级）。
        const long wsId = 13;
        var emp = AlgorithmTestData.Employee(1, "E001", "甲", "楼面");
        var skill = AlgorithmTestData.Skill(1, wsId);
        var dates = new List<DateParameterInput> { AlgorithmTestData.Day(TestDate) };
        // 碎片块 13:00-14:00（1h < 6.5h）
        var reqs = ReqSlots(new TimeSpan(13, 0, 0), new TimeSpan(14, 0, 0), wsId);
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { emp },
            new List<SkillInput> { skill },
            dates,
            new List<ShiftTemplateInput>(),
            reqs,
            minDailyWorkHours: 6.5m);

        var workstationAssignments = new List<WorkstationAssignment>();
        var residualTemplates = new List<ShiftTemplateInput>();

        var residual = ResidualGapFiller.Fill(
            input,
            new List<RestDayAssignment>(),
            new List<ShiftAssignment>(),
            workstationAssignments,
            residualTemplates);

        // 无兼职可用 → 碎片块缺口保留
        Assert.Equal(1, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(13, 0, 0))));
        Assert.Equal(1, residual.GetValueOrDefault((wsId, TestDate, new TimeSpan(13, 30, 0))));
        Assert.Empty(workstationAssignments);
    }
}
