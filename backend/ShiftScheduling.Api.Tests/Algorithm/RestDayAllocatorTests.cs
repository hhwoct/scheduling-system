using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Algorithm.Steps;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>休息日分配器单元测试。</summary>
public sealed class RestDayAllocatorTests
{
    private static readonly DateOnly Start = new(2026, 8, 3); // 周一

    private static SchedulingInput InputWithDepartments(params (long Id, string Dept)[] employees)
        => AlgorithmTestData.Build(
            employees.Select(e => AlgorithmTestData.Employee(e.Id, "E" + e.Id, "员工" + e.Id, e.Dept)).ToList(),
            new List<SkillInput>(),
            AlgorithmTestData.Days(Start, 7),
            new List<ShiftTemplateInput>(),
            new List<StaffingRequirementInput>());

    [Fact]
    public void Allocate_7DayPeriod_GivesOneRestDayPerEmployee()
    {
        var input = InputWithDepartments((1, "楼面"), (2, "厨房"), (3, "管理"));
        var result = new RestDayAllocator().Allocate(input);

        // round(4 * 7 / 30) = 1 天
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result.Count(x => x.EmployeeId == 1));
        Assert.Equal(1, result.Count(x => x.EmployeeId == 2));
        Assert.Equal(1, result.Count(x => x.EmployeeId == 3));
    }

    [Fact]
    public void Allocate_ServiceEmployee_NeverRestsOnFridayOrSaturday()
    {
        var input = InputWithDepartments((1, "楼面"), (2, "厨房"), (3, "吧台"));
        var result = new RestDayAllocator().Allocate(input);

        // 8/7 是周五、8/8 是周六
        Assert.DoesNotContain(result, x => x.EmployeeId == 1 && (x.WorkDate.DayOfWeek == DayOfWeek.Friday || x.WorkDate.DayOfWeek == DayOfWeek.Saturday));
        Assert.DoesNotContain(result, x => x.EmployeeId == 2 && (x.WorkDate.DayOfWeek == DayOfWeek.Friday || x.WorkDate.DayOfWeek == DayOfWeek.Saturday));
        Assert.DoesNotContain(result, x => x.EmployeeId == 3 && (x.WorkDate.DayOfWeek == DayOfWeek.Friday || x.WorkDate.DayOfWeek == DayOfWeek.Saturday));
    }

    [Fact]
    public void Allocate_AdminEmployee_CanRestOnPeakDays()
    {
        // 7 名行政员工 × 7 天 → 每天配额 1，周五/周六（高峰日）也有配额且只有行政可休
        var input = AlgorithmTestData.Build(
            Enumerable.Range(1, 7).Select(i => AlgorithmTestData.Employee(i, "E" + i, "员工" + i, "管理")).ToList(),
            new List<SkillInput>(),
            AlgorithmTestData.Days(Start, 7),
            new List<ShiftTemplateInput>(),
            new List<StaffingRequirementInput>());

        var result = new RestDayAllocator().Allocate(input);

        // 行政员工允许在周五/周六休息：至少存在一条行政员工的休息落在周五或周六
        var adminRestOnPeak = result.Where(x => x.WorkDate.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday).ToList();
        Assert.NotEmpty(adminRestOnPeak);
    }

    [Fact]
    public void Allocate_ZeroRestDaysTarget_ReturnsEmpty()
    {
        // 2 天周期：round(4 * 2 / 30) = 0 → 不分配休息
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E1", "员工1", "楼面") },
            new List<SkillInput>(),
            AlgorithmTestData.Days(Start, 2),
            new List<ShiftTemplateInput>(),
            new List<StaffingRequirementInput>());

        var result = new RestDayAllocator().Allocate(input);
        Assert.Empty(result);
    }

    [Fact]
    public void Allocate_NoDates_ReturnsEmpty()
    {
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E1", "员工1", "楼面") },
            new List<SkillInput>(),
            new List<DateParameterInput>(),
            new List<ShiftTemplateInput>(),
            new List<StaffingRequirementInput>());

        var result = new RestDayAllocator().Allocate(input);
        Assert.Empty(result);
    }

    [Fact]
    public void Allocate_QuotaSpreadsAcrossDays_BreaksLongWorkStreaks()
    {
        // 6 名服务员工 × 14 天，每人目标 2 天休息；
        // 休息分配后任何员工连续工作不得超过 MaxConsecutiveWorkDays(6)
        var employees = Enumerable.Range(1, 6).Select(i => AlgorithmTestData.Employee(i, "E" + i, "员工" + i, "楼面")).ToList();
        var input = AlgorithmTestData.Build(
            employees,
            new List<SkillInput>(),
            AlgorithmTestData.Days(Start, 14),
            new List<ShiftTemplateInput>(),
            new List<StaffingRequirementInput>(),
            maxConsecutiveWorkDays: 6);

        var result = new RestDayAllocator().Allocate(input);

        var dates = input.DateParameters.Select(d => d.WorkDate).OrderBy(d => d).ToList();
        foreach (var emp in employees)
        {
            var restSet = result.Where(x => x.EmployeeId == emp.Id).Select(x => x.WorkDate).ToHashSet();
            var streak = 0;
            var maxStreak = 0;
            foreach (var date in dates)
            {
                if (restSet.Contains(date))
                {
                    streak = 0;
                }
                else
                {
                    streak++;
                    maxStreak = Math.Max(maxStreak, streak);
                }
            }

            Assert.True(maxStreak <= 6, $"员工 {emp.Id} 最长连续工作 {maxStreak} 天，超过上限 6 天");
        }
    }
}