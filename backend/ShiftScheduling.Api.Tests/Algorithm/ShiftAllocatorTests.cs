using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Algorithm.Steps;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>班次分配器单元测试。</summary>
public sealed class ShiftAllocatorTests
{
    private static readonly DateOnly Start = new(2026, 8, 3); // 周一

    private static SchedulingInput BuildInput(
        List<EmployeeInput> employees,
        List<SkillInput> skills,
        ShiftTemplateInput shift,
        int requiredPerSlot,
        List<RestDayAssignment>? restDays = null)
    {
        return AlgorithmTestData.Build(
            employees,
            skills,
            AlgorithmTestData.Days(Start, 7),
            new List<ShiftTemplateInput> { shift },
            AlgorithmTestData.ReqsForShift("WORKDAY", shift, requiredPerSlot));
    }

    [Fact]
    public void Allocate_AssignsSkilledEmployeesToMatchingShift()
    {
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10);
        var input = BuildInput(
            new List<EmployeeInput>
            {
                AlgorithmTestData.Employee(1, "E001", "甲", "楼面"),
                AlgorithmTestData.Employee(2, "E002", "乙", "厨房")
            },
            new List<SkillInput>
            {
                AlgorithmTestData.Skill(1, 10) // 甲会楼面
                // 乙无技能
            },
            shift,
            requiredPerSlot: 1);

        var result = new ShiftAllocator().Allocate(input, new List<RestDayAssignment>());

        Assert.NotEmpty(result);
        Assert.All(result, a => Assert.Equal(1, a.EmployeeId)); // 只有甲被排
        Assert.All(result, a => Assert.Equal("S4", a.ShiftCode));
    }

    [Fact]
    public void Allocate_RespectsRestDays()
    {
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10);
        var input = BuildInput(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E001", "甲", "楼面") },
            new List<SkillInput> { AlgorithmTestData.Skill(1, 10) },
            shift,
            requiredPerSlot: 1);

        // 周一休息 → 周一不应有排班
        var restDays = new List<RestDayAssignment> { new(1, Start) };
        var result = new ShiftAllocator().Allocate(input, restDays);

        Assert.DoesNotContain(result, a => a.WorkDate == Start);
        Assert.Contains(result, a => a.WorkDate == Start.AddDays(1));
    }

    [Fact]
    public void Allocate_DoesNotExceedWeeklyHourLimit()
    {
        // 每个班次 10 小时，input 级周上限 20 小时 → 最多 2 个班次/周
        var shift = AlgorithmTestData.Shift(1, "S4", "长班", new TimeSpan(9, 0, 0), new TimeSpan(19, 0, 0), 0, 7, 10);
        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E001", "甲", "楼面") },
            new List<SkillInput> { AlgorithmTestData.Skill(1, 10) },
            AlgorithmTestData.Days(Start, 7),
            new List<ShiftTemplateInput> { shift },
            AlgorithmTestData.ReqsForShift("WORKDAY", shift, 1),
            maxWeeklyHours: 20);

        var result = new ShiftAllocator().Allocate(input, new List<RestDayAssignment>());

        // 10h/班、上限 20h → 每周最多 2 班
        Assert.True(result.Count <= 2, $"实际排了 {result.Count} 班，超出周工时上限约束");
    }

    [Fact]
    public void Allocate_NoOverlapBetweenConsecutiveDaysCrossDayShift()
    {
        // 22:00-02:00 跨天班：员工排周一班后，周二 00:00-01:30 已被占用，
        // 不应再排覆盖该时段的周二早班
        var nightShift = AlgorithmTestData.Shift(1, "S7", "夜班", new TimeSpan(22, 0, 0), new TimeSpan(2, 0, 0), 1, 1, 8);
        var morningShift = AlgorithmTestData.Shift(2, "S2", "早班", new TimeSpan(0, 0, 0), new TimeSpan(4, 0, 0), 0, 3, 8);

        var requirements = AlgorithmTestData.ReqsForShift("WORKDAY", nightShift, 1, 8);
        requirements.AddRange(AlgorithmTestData.ReqsForShift("WORKDAY", morningShift, 1, 8));

        var input = AlgorithmTestData.Build(
            new List<EmployeeInput> { AlgorithmTestData.Employee(1, "E001", "甲", "厨房") },
            new List<SkillInput> { AlgorithmTestData.Skill(1, 8) },
            AlgorithmTestData.Days(Start, 3),
            new List<ShiftTemplateInput> { nightShift, morningShift },
            requirements);

        var generatedTemplates = new List<ShiftTemplateInput>();
        var result = new ShiftAllocator().Allocate(input, new List<RestDayAssignment>(), generatedTemplates);

        // 真实不变量：同一员工的占用时段（含跨天午夜回绕）不得重叠
        // 动态生成的 D 班次（负数模板 Id）也要纳入占用计算
        var shiftById = input.ShiftTemplates.Concat(generatedTemplates).ToDictionary(s => s.Id);
        var busy = new HashSet<(DateOnly Date, TimeSpan Slot)>();
        foreach (var a in result)
        {
            var template = shiftById[a.ShiftTemplateId];
            foreach (var slot in SchedulingTimeHelper.GetShiftSlots(template.StartTime, template.EndTime, template.IsCrossDay))
            {
                var calendarDate = SchedulingTimeHelper.SlotCalendarDate(slot, template.StartTime, a.WorkDate);
                Assert.True(busy.Add((calendarDate, slot)), $"员工 {a.EmployeeId} 时段 {(calendarDate, slot)} 被重复占用");
            }
        }
    }

    [Fact]
    public void Allocate_GeneratesDemandShiftsForUncoveredGap()
    {
        // 模板班次只覆盖 19:00-22:00，但需求在 22:00-23:30 也存在
        var shift = AlgorithmTestData.Shift(1, "S4", "楼面A班", new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0), 0, 7, 10);
        var requirements = new List<StaffingRequirementInput>();
        foreach (var slot in SchedulingTimeHelper.GetShiftSlots(new TimeSpan(19, 0, 0), new TimeSpan(23, 30, 0), 0))
        {
            requirements.Add(AlgorithmTestData.Req("WORKDAY", 10, slot, 1));
        }

        var input = AlgorithmTestData.Build(
            new List<EmployeeInput>
            {
                AlgorithmTestData.Employee(1, "E001", "甲", "楼面"),
                AlgorithmTestData.Employee(2, "E002", "乙", "楼面")
            },
            new List<SkillInput> { AlgorithmTestData.Skill(1, 10), AlgorithmTestData.Skill(2, 10) },
            AlgorithmTestData.Days(Start, 1),
            new List<ShiftTemplateInput> { shift },
            requirements);

        var generatedTemplates = new List<ShiftTemplateInput>();
        var result = new ShiftAllocator().Allocate(input, new List<RestDayAssignment>(), generatedTemplates);

        // 缺口应生成 D1 临时班次并被排班
        Assert.Contains(generatedTemplates, t => t.Code == "D1");
        Assert.Contains(result, a => a.ShiftCode == "D1");
    }
}