using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>跨天班次的班中休息：休息时段按实际日历日期归属次日凌晨，并按营业日口径取需求。</summary>
public sealed class CrossDayBreakTests
{
    [Fact]
    public void CrossDayShift_BreakAfterMidnight_UsesBusinessDayDemand()
    {
        var testDate = new DateOnly(2026, 8, 1); // 周六 HOLIDAY（当日营业日类型）
        var employees = new List<EmployeeInput>
        {
            new(1, "E001", "员工1", "厨房", "厨房", 48),
            new(2, "E002", "员工2", "厨房", "厨房", 48)
        };
        var skills = new List<SkillInput>
        {
            new(1, 8, 5, 1),
            new(2, 8, 5, 1)
        };
        var s7 = new ShiftTemplateInput(1, "S7", "厨房A班", new TimeSpan(18, 0, 0), new TimeSpan(3, 0, 0), 1, 1, new List<long> { 8 });
        var shifts = new List<ShiftTemplateInput> { s7 };

        var staffing = new List<StaffingRequirementInput>();
        foreach (var slot in SchedulingTimeHelper.GetShiftSlots(s7.StartTime, s7.EndTime, 1))
        {
            if (slot < TimeSpan.FromHours(6))
            {
                // 次日凌晨：营业日口径应取 HOLIDAY(当日类型) 最少1/最好2（两名在岗满足上限且冗余 1，可休息）；
                // 若错误地取 WORKDAY 最少5/最好5，则会判定无人顶岗（旧缺陷）。
                staffing.Add(new StaffingRequirementInput("HOLIDAY", 8, slot, 1, 2));
                staffing.Add(new StaffingRequirementInput("WORKDAY", 8, slot, 5, 5));
            }
            else
            {
                // 18:00-23:30 需求 2：两人在岗刚好覆盖，休息即缺人 → 不可在此休息
                staffing.Add(new StaffingRequirementInput("HOLIDAY", 8, slot, 2));
            }
        }

        var input = new SchedulingInput(
            1, testDate, testDate.AddDays(1),
            employees, skills,
            new List<DateParameterInput>
            {
                new(testDate, 7, "HOLIDAY", 1, 0),
                new(testDate.AddDays(1), 1, "WORKDAY", 0, 0)
            },
            shifts, staffing,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput> { new(TimeSpan.FromHours(20), TimeSpan.FromHours(22)) },
            new Dictionary<long, bool>(),
            new Dictionary<long, string>(),
            4, 6, 10, 0m);

        var shiftAssignments = new List<ShiftAssignment>
        {
            new(1, testDate, 1, "S7"),
            new(2, testDate, 1, "S7")
        };
        var wsAssignments = new List<WorkstationAssignment>();
        foreach (var empId in new[] { 1L, 2L })
        {
            foreach (var slot in SchedulingTimeHelper.GetShiftSlots(s7.StartTime, s7.EndTime, 1))
            {
                wsAssignments.Add(new WorkstationAssignment(empId, testDate, slot, 8, 5, 1));
            }
        }

        var issues = new List<ScheduleIssueOutput>();
        var breaks = new BreakAllocator().Allocate(input, shiftAssignments, wsAssignments, issues);

        // 员工 1 的休息应落在次日 00:00（凌晨需求冗余充足），且无无人顶岗告警
        var b = Assert.Single(breaks, x => x.EmployeeId == 1);
        Assert.Equal(TimeSpan.Zero, b.BreakStartTime);
        Assert.Equal(testDate, b.WorkDate); // 持久化约定：休息记录仍挂在班次开始日

        Assert.DoesNotContain(issues, i => i.IssueType == "BREAK_UNCOVERED" && i.EmployeeId == 1);
    }
}
