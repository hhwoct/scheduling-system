using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Algorithm.Steps;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>
/// 兼职集中度回归测试：算法应尽量复用同一名兼职（少用兼职人员）。
/// 例如连续 6 天的传送需求应全部由兼传送A承担，而不是 A 三天、B 三天。
/// </summary>
public sealed class ShiftAllocatorPartTimeTests
{
    [Fact]
    public void PartTimeDemand_ConcentratesOnSingleEmployee()
    {
        var workstationId = 9L;
        var employees = new List<EmployeeInput>
        {
            new(1, "E001", "全职无技能", "楼面", "服务岗", 48m, 0),
            new(101, "E101", "兼传送A", "兼职", "传送岗", 32m, 1),
            new(102, "E102", "兼传送B", "兼职", "传送岗", 32m, 1)
        };

        var skills = new List<SkillInput>
        {
            new(101, workstationId, 5, 1),
            new(102, workstationId, 5, 1)
        };

        var dates = new List<DateParameterInput>();
        for (var i = 0; i < 6; i++)
        {
            var day = new DateOnly(2026, 8, 17).AddDays(i);
            dates.Add(new DateParameterInput(day, (int)day.DayOfWeek + 1, "WORKDAY", 0, 0));
        }

        var shift = new ShiftTemplateInput(
            5, "S5", "楼面B班", new TimeSpan(18, 0, 0), new TimeSpan(2, 0, 0), 1, 8,
            new List<long> { workstationId });

        var staffing = new List<StaffingRequirementInput>();
        foreach (var slot in SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay))
        {
            staffing.Add(new StaffingRequirementInput("WORKDAY", workstationId, slot, 1));
        }

        var input = new SchedulingInput(
            1,
            dates.First().WorkDate,
            dates.Last().WorkDate,
            employees,
            skills,
            dates,
            new List<ShiftTemplateInput> { shift },
            staffing,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput>(),
            new Dictionary<long, bool> { [workstationId] = true },
            4,
            70m, // 周工时上限足够高，不阻碍兼职集中排班
            14,
            10);

        var assignments = new ShiftAllocator().Allocate(input, new List<RestDayAssignment>());

        var partTimeUsed = assignments
            .Select(a => a.EmployeeId)
            .Where(id => id == 101 || id == 102)
            .Distinct()
            .ToList();

        Assert.Equal(6, assignments.Count);
        Assert.Single(partTimeUsed); // 6 天全部由同一名兼职承担，而不是两人轮换
        Assert.Equal(101, partTimeUsed[0]);
    }
}
