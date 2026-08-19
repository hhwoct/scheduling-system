using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

public sealed class SchedulingEngineTests
{
    private static SchedulingInput BuildTestInput()
    {
        var employees = new List<EmployeeInput>
        {
            new(1, "E001", "张店长", "管理", "管理岗", 48),
            new(2, "E010", "冯厨房", "厨房", "厨房岗", 48),
            new(3, "E015", "沈服务", "楼面", "服务岗", 48)
        };

        var workstations = new Dictionary<string, long>
        {
            ["MANAGER"] = 1,
            ["KITCHEN"] = 8,
            ["SERVICE"] = 10
        };

        var skills = new List<SkillInput>
        {
            new(1, workstations["MANAGER"], 5, 1),
            new(2, workstations["KITCHEN"], 5, 1),
            new(3, workstations["SERVICE"], 4, 1)
        };

        var dates = new List<DateParameterInput>();
        for (var i = 0; i < 7; i++)
        {
            var day = new DateOnly(2026, 8, 1).AddDays(i);
            dates.Add(new DateParameterInput(day, (int)day.DayOfWeek + 1, "WORKDAY", 0, 0));
        }

        var shiftTemplates = new List<ShiftTemplateInput>
        {
            new(1, "S1", "行政仓管/文员班", new TimeSpan(13, 0, 0), new TimeSpan(22, 0, 0), 0, 5, new List<long> { workstations["MANAGER"] }),
            new(2, "S7", "厨房A班", new TimeSpan(18, 0, 0), new TimeSpan(3, 0, 0), 1, 1, new List<long> { workstations["KITCHEN"] }),
            new(3, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(4, 0, 0), 1, 7, new List<long> { workstations["SERVICE"] })
        };

        var staffing = new List<StaffingRequirementInput>();
        foreach (var shift in shiftTemplates)
        {
            var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);
            foreach (var slot in slots)
            {
                staffing.Add(new StaffingRequirementInput(
                    "WORKDAY",
                    shift.WorkstationIds[0],
                    slot,
                    shift.Code == "S7" ? 2 : 1));
            }
        }

        return new SchedulingInput(
            1,
            dates.First().WorkDate,
            dates.Last().WorkDate,
            employees,
            skills,
            dates,
            shiftTemplates,
            staffing,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput>(),
            new Dictionary<long, bool>(),
            4,
            48m,
            6,
            10);
    }

    [Fact]
    public void RestDayAllocator_AssignsExpectedNumberOfRestDays()
    {
        var input = BuildTestInput();
        var engine = new SchedulingEngine(null!);

        var output = engine.Execute(input);

        var restDaysByEmployee = output.RestDays
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 7 天周期按比例折算：round(4 * 7 / 30) = 1 天
        Assert.All(input.Employees, e => Assert.Equal(1, restDaysByEmployee[e.Id]));
    }

    [Fact]
    public void ShiftAllocator_DoesNotAssignShiftsForUnskilledEmployees()
    {
        var input = BuildTestInput();
        var engine = new SchedulingEngine(null!);

        var output = engine.Execute(input);

        foreach (var assignment in output.ShiftAssignments)
        {
            // D 班次（模板 id 为负，动态生成）不在输入模板表中，跳过
            if (assignment.ShiftTemplateId < 0)
            {
                continue;
            }

            var shift = input.ShiftTemplates.First(s => s.Id == assignment.ShiftTemplateId);
            var employee = input.Employees.First(e => e.Id == assignment.EmployeeId);

            if (employee.Id == 2)
            {
                Assert.Equal("S7", shift.Code);
            }
            else if (employee.Id == 3)
            {
                Assert.Equal("S4", shift.Code);
            }
            else
            {
                Assert.Equal("S1", shift.Code);
            }
        }
    }

    [Fact]
    public void DaySummaries_WorkedDaysHavePositiveHours()
    {
        var input = BuildTestInput();
        var engine = new SchedulingEngine(null!);

        var output = engine.Execute(input);

        var workedSummaries = output.DaySummaries.Where(x => x.IsRestDay == 0).ToList();
        Assert.NotEmpty(workedSummaries);
        Assert.All(workedSummaries, s => Assert.True(s.WorkHours > 0));
    }

    [Fact]
    public void CrossDayShift_GeneratesSlotsAcrossMidnight()
    {
        var shift = new ShiftTemplateInput(
            2,
            "S7",
            "厨房A班",
            new TimeSpan(18, 0, 0),
            new TimeSpan(3, 0, 0),
            1,
            1,
            new List<long> { 8 });

        var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);

        Assert.Contains(new TimeSpan(18, 0, 0), slots);
        Assert.Contains(new TimeSpan(23, 30, 0), slots);
        Assert.Contains(TimeSpan.Zero, slots);
        Assert.Contains(new TimeSpan(2, 30, 0), slots);
        Assert.DoesNotContain(new TimeSpan(3, 0, 0), slots);
        Assert.Equal(18, slots.Count);
    }

    [Fact]
    public void Execute_ProducesWorkstationAssignmentsAndGapIssues()
    {
        var input = BuildTestInput();
        var engine = new SchedulingEngine(null!);

        var output = engine.Execute(input);

        Assert.NotEmpty(output.WorkstationAssignments);
        Assert.Contains(output.Issues, x => x.IssueType == "STAFFING_GAP");
    }
}
