using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>
/// 闭店空窗（06:00~12:30）需求回归测试：
/// 班表时间轴为 13:00 开门 → 次日 06:00 打烊，06:00 起属于闭店时段。
/// 即使人数需求数据误配了 06:00 的需求，引擎也不得生成该时段的
/// D 临时班次 / 工作站分配 / 岗位缺口（历史上曾产生界面不可见的幽灵时段）。
/// </summary>
public sealed class ClosedMorningDemandTests
{
    private static readonly DateOnly D0 = new(2026, 9, 7); // 周一

    [Fact]
    public void ClosedMorningDemand_IsIgnored_NoGhostAssignmentsOrGaps()
    {
        var employees = new List<EmployeeInput>
        {
            new(1, "E001", "冯厨房", "厨房", "厨房岗", 48)
        };
        var skills = new List<SkillInput>
        {
            new(1, 8, 5, 1)
        };
        var shiftTemplates = new List<ShiftTemplateInput>
        {
            new(1, "S7", "厨房A班", new TimeSpan(16, 0, 0), new TimeSpan(1, 30, 0), 1, 1, new List<long> { 8 })
        };
        var staffing = new List<StaffingRequirementInput>
        {
            // 营业时段正常需求 16:00-17:00
            new("WORKDAY", 8, new TimeSpan(16, 0, 0), 1),
            new("WORKDAY", 8, new TimeSpan(16, 30, 0), 1),
            // 误配：闭店空窗 06:00（打烊后）需求
            new("WORKDAY", 8, new TimeSpan(6, 0, 0), 1),
            new("WORKDAY", 8, new TimeSpan(6, 30, 0), 1)
        };

        var input = new SchedulingInput(
            1, D0, D0,
            employees, skills,
            new List<DateParameterInput>
            {
                new(D0, 7, "WORKDAY", 0, 0)
            },
            shiftTemplates, staffing,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput>(),
            new Dictionary<long, bool>(),
            new Dictionary<long, string>(),
            4, 6, 10, 0m);

        var engine = new SchedulingEngine(null!);
        var output = engine.Execute(input);

        // 闭店时段不得有任何工作站分配
        Assert.DoesNotContain(output.WorkstationAssignments,
            a => a.TimeSlot >= TimeSpan.FromHours(6) && a.TimeSlot < TimeSpan.FromHours(13));

        // 闭店时段不得有 D 临时班次的工作站分配（D 班次为负模板 Id）
        Assert.DoesNotContain(output.WorkstationAssignments,
            a => a.ShiftTemplateId < 0 && a.TimeSlot >= TimeSpan.FromHours(6) && a.TimeSlot < TimeSpan.FromHours(13));

        // 闭店时段不得上报岗位缺口
        Assert.DoesNotContain(output.Issues,
            i => i.TimeSlot >= TimeSpan.FromHours(6) && i.TimeSlot < TimeSpan.FromHours(13));

        // 营业时段需求正常覆盖（16:00-17:00 由 S7 覆盖）
        Assert.Contains(output.WorkstationAssignments,
            a => a.WorkDate == D0 && a.TimeSlot == new TimeSpan(16, 0, 0) && a.WorkstationId == 8);
    }
}
