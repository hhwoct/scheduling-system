using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>
/// 跨午夜营业日口径的回归测试：
/// 跨天班次的午夜回绕部分必须匹配【次日】凌晨的需求（该凌晨归属班次开始日所在营业日），
/// 而不是错误扣减班次开始日自己凌晨（归属上一营业日）的需求。
/// </summary>
public sealed class CrossDayDemandTests
{
    private static readonly DateOnly D0 = new(2026, 8, 1); // 周六 WORKDAY
    private static readonly DateOnly D1 = D0.AddDays(1);   // 周日 HOLIDAY

    private static SchedulingInput BuildInput(int earlyMorningDemand)
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
            new(1, "S7", "厨房A班", new TimeSpan(22, 0, 0), new TimeSpan(2, 0, 0), 1, 1, new List<long> { 8 })
        };
        // WORKDAY 需求：22:00-23:30（D0 晚市）+ 00:00-01:30（D1 凌晨，归属 D0 营业日）
        // HOLIDAY 需求：00:00-01:30（D2 凌晨，归属 D1 营业日，不在周期内）
        var staffing = new List<StaffingRequirementInput>();
        foreach (var slot in new[] { new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 0, 0), new TimeSpan(23, 30, 0) })
        {
            staffing.Add(new StaffingRequirementInput("WORKDAY", 8, slot, 1));
        }
        foreach (var slot in new[] { TimeSpan.Zero, new TimeSpan(0, 30, 0), new TimeSpan(1, 0, 0), new TimeSpan(1, 30, 0) })
        {
            staffing.Add(new StaffingRequirementInput("WORKDAY", 8, slot, earlyMorningDemand));
            staffing.Add(new StaffingRequirementInput("HOLIDAY", 8, slot, 1));
        }

        return new SchedulingInput(
            1, D0, D1,
            employees, skills,
            new List<DateParameterInput>
            {
                new(D0, 7, "WORKDAY", 0, 0),
                new(D1, 1, "HOLIDAY", 1, 0)
            },
            shiftTemplates, staffing,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput>(),
            new Dictionary<long, bool>(),
            new Dictionary<long, string>(),
            4, 6, 10, 0m);
    }

    [Fact]
    public void CrossDayShift_TailCoversNextDayEarlyMorningDemand()
    {
        var input = BuildInput(earlyMorningDemand: 1);
        var engine = new SchedulingEngine(null!);

        var output = engine.Execute(input);

        // S7（22:00-02:00）排给员工 1，开始日为 D0
        var s7 = Assert.Single(output.ShiftAssignments);
        Assert.Equal("S7", s7.ShiftCode);
        Assert.Equal(D0, s7.WorkDate);

        // 午夜回绕部分（00:00-01:30）已生成工作站分配（挂在班次开始日 D0 下）
        Assert.Contains(output.WorkstationAssignments,
            a => a.WorkDate == D0 && a.TimeSlot == TimeSpan.Zero && a.WorkstationId == 8);

        // D1 凌晨需求（归属 D0 营业日）已被 S7 的跨天尾巴覆盖 → 无任何缺口
        Assert.DoesNotContain(output.Issues, i => i.IssueType == "STAFFING_GAP");
    }

    [Fact]
    public void CrossDayShift_UncoveredNextDayEarlyMorning_ReportsGapOnNextCalendarDay()
    {
        // D1 凌晨需要 2 人，仅 1 名员工跨天覆盖 → 缺口 1 人必须上报在【次日日历日 D1】
        var input = BuildInput(earlyMorningDemand: 2);
        var engine = new SchedulingEngine(null!);

        var output = engine.Execute(input);

        var gap = output.Issues.FirstOrDefault(i => i.IssueType == "STAFFING_GAP" && i.WorkDate == D1 && i.TimeSlot == TimeSpan.Zero);
        Assert.NotNull(gap);
        // 短缺口 = 2(需求) - 1(跨天尾巴覆盖)；若按旧缺陷（扣减丢失/匹配错营业日）会显示缺 2 人
        Assert.Contains("缺 1 人", gap!.Description);
    }
}
