using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

public sealed class BreakAllocatorTests
{
    private static readonly DateOnly TestDate = new(2026, 8, 1);

    private static SchedulingInput BuildInput(
        List<EmployeeInput> employees,
        List<SkillInput> skills,
        List<ShiftTemplateInput> shifts,
        List<StaffingRequirementInput> staffing)
    {
        return new SchedulingInput(
            1, TestDate, TestDate,
            employees, skills,
            new List<DateParameterInput> { new(TestDate, 7, "WORKDAY", 0, 0) },
            shifts, staffing,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput> { new(TimeSpan.FromHours(20), TimeSpan.FromHours(22)) },
            new Dictionary<long, bool>(),
            new Dictionary<long, string>(),
            4, 6, 10, 0m);
    }

    private static (List<ShiftAssignment> Shifts, List<WorkstationAssignment> Ws) BuildAssignments(
        List<(long EmpId, long ShiftId)> shiftPairs,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, (long WsId, int Score)> primaryWs)
    {
        var shifts = new List<ShiftAssignment>();
        var ws = new List<WorkstationAssignment>();
        foreach (var (empId, shiftId) in shiftPairs)
        {
            var tpl = shiftById[shiftId];
            shifts.Add(new ShiftAssignment(empId, TestDate, shiftId, tpl.Code));
            foreach (var slot in SchedulingTimeHelper.GetShiftSlots(tpl.StartTime, tpl.EndTime, tpl.IsCrossDay))
            {
                ws.Add(new WorkstationAssignment(empId, TestDate, slot, primaryWs[empId].WsId, primaryWs[empId].Score, shiftId));
            }
        }

        return (shifts, ws);
    }

    [Fact]
    public void ShortShift_Under4Hours_GetsNoBreak()
    {
        var employees = new List<EmployeeInput> { new(1, "E001", "员工1", "楼面", "服务", 48) };
        var skills = new List<SkillInput> { new(1, 10, 5, 1) };
        var shifts = new List<ShiftTemplateInput>
        {
            new(1, "S9", "短班", new TimeSpan(14, 0, 0), new TimeSpan(17, 0, 0), 0, 9, new List<long> { 10 })
        };
        var staffing = new List<StaffingRequirementInput>
        {
            new("WORKDAY", 10, new TimeSpan(14, 0, 0), 1),
            new("WORKDAY", 10, new TimeSpan(16, 30, 0), 1)
        };
        var input = BuildInput(employees, skills, shifts, staffing);
        var (shiftAssignments, wsAssignments) = BuildAssignments(
            new List<(long, long)> { (1, 1) },
            shifts.ToDictionary(x => x.Id),
            new Dictionary<long, (long, int)> { [1] = (10, 5) });
        var issues = new List<ScheduleIssueOutput>();

        var breaks = new BreakAllocator().Allocate(input, shiftAssignments, wsAssignments, issues);

        Assert.Empty(breaks);
        Assert.Empty(issues);
    }

    [Fact]
    public void NormalShift_BreakAvoidsPeakWindow_AndSoleWorkerReportsUncovered()
    {
        // 13:00-22:00（9h）单人班；高峰 20:00-22:00。
        // 可休窗口 15:00-21:00，挖掉高峰后应为 15:00-20:00。
        var employees = new List<EmployeeInput> { new(1, "E001", "员工1", "楼面", "服务", 48) };
        var skills = new List<SkillInput> { new(1, 10, 5, 1) };
        var shifts = new List<ShiftTemplateInput>
        {
            new(1, "S1", "行政班", new TimeSpan(13, 0, 0), new TimeSpan(22, 0, 0), 0, 5, new List<long> { 10 })
        };
        var staffing = SchedulingTimeHelper.GetShiftSlots(shifts[0].StartTime, shifts[0].EndTime, 0)
            .Select(slot => new StaffingRequirementInput("WORKDAY", 10, slot, 1))
            .ToList();
        var input = BuildInput(employees, skills, shifts, staffing);
        var (shiftAssignments, wsAssignments) = BuildAssignments(
            new List<(long, long)> { (1, 1) },
            shifts.ToDictionary(x => x.Id),
            new Dictionary<long, (long, int)> { [1] = (10, 5) });
        var issues = new List<ScheduleIssueOutput>();

        var breaks = new BreakAllocator().Allocate(input, shiftAssignments, wsAssignments, issues);

        var b = Assert.Single(breaks);
        Assert.True(b.BreakStartTime >= TimeSpan.FromHours(15), $"休息 {b.BreakStartTime} 不应早于上班 2 小时");
        Assert.True(b.BreakStartTime < TimeSpan.FromHours(20), $"休息 {b.BreakStartTime} 不应与高峰 20:00-22:00 重叠");

        // 独苗岗位无人顶岗 → 输出 BREAK_UNCOVERED
        Assert.Contains(issues, i => i.IssueType == "BREAK_UNCOVERED" && i.EmployeeId == 1);
    }

    [Fact]
    public void BorrowCoversBreak_NoUncoveredIssue()
    {
        // 员工1 独苗站10（需求1，需休息）；员工2 在站20（无需求），有站10技能 → 借调成功
        var employees = new List<EmployeeInput>
        {
            new(1, "E001", "员工1", "楼面", "服务", 48),
            new(2, "E002", "员工2", "厨房", "厨房", 48)
        };
        var skills = new List<SkillInput>
        {
            new(1, 10, 5, 1),
            new(2, 20, 5, 1),
            new(2, 10, 4, 1) // 员工2 也有站10技能（主技能）
        };
        var shifts = new List<ShiftTemplateInput>
        {
            new(1, "S1", "行政班", new TimeSpan(13, 0, 0), new TimeSpan(22, 0, 0), 0, 5, new List<long> { 10 }),
            new(2, "S3", "帮工班", new TimeSpan(15, 0, 0), new TimeSpan(20, 0, 0), 0, 3, new List<long> { 20 })
        };
        var staffing = new List<StaffingRequirementInput>();
        foreach (var slot in SchedulingTimeHelper.GetShiftSlots(shifts[0].StartTime, shifts[0].EndTime, 0))
        {
            staffing.Add(new StaffingRequirementInput("WORKDAY", 10, slot, 1));
        }

        var input = BuildInput(employees, skills, shifts, staffing);
        var (shiftAssignments, wsAssignments) = BuildAssignments(
            new List<(long, long)> { (1, 1), (2, 2) },
            shifts.ToDictionary(x => x.Id),
            new Dictionary<long, (long, int)> { [1] = (10, 5), [2] = (20, 5) });
        var issues = new List<ScheduleIssueOutput>();

        var breaks = new BreakAllocator().Allocate(input, shiftAssignments, wsAssignments, issues);

        // 员工1 独苗站10 必须靠借调覆盖；员工2 自己的班（15:00-20:00 共5h）也需要休息
        var b1 = Assert.Single(breaks, x => x.EmployeeId == 1);
        Assert.Equal(2, b1.CoverEmployeeId); // 借调人 = 员工2
        Assert.DoesNotContain(issues, i => i.IssueType == "BREAK_UNCOVERED");
    }
}
