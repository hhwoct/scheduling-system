using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>
/// 单时段人数上限回归测试：整段班次按窗口峰值配人、重叠班次叠加时，
/// 任一（日历日, 时段, 工作站）的覆盖人数都不得超过该时段配置的「最好人数」；
/// 被上限压住的缺口由 D 班次按缺口块回填（起止时间跟随缺口，不多排）。
/// </summary>
public sealed class OverStaffingCeilingTests
{
    private static readonly DateOnly D0 = new(2026, 8, 3); // 周一 WORKDAY
    private static readonly DateOnly D1 = D0.AddDays(1);   // 周二 WORKDAY

    internal static SchedulingInput BuildInput()
    {
        var employees = new List<EmployeeInput>
        {
            new(1, "E001", "员工1", "厨房", "厨房", 48),
            new(2, "E002", "员工2", "厨房", "厨房", 48),
            new(3, "E003", "员工3", "厨房", "厨房", 48)
        };
        var skills = new List<SkillInput>
        {
            new(1, 8, 5, 1),
            new(2, 8, 5, 1),
            new(3, 8, 5, 1)
        };
        // 重叠的两个跨天班次：T1 20:00-04:00、T2 16:00-02:00（模拟门店实际模板）
        var shiftTemplates = new List<ShiftTemplateInput>
        {
            new(1, "T1", "晚班A", new TimeSpan(20, 0, 0), new TimeSpan(4, 0, 0), 1, 1, new List<long> { 8 }),
            new(2, "T2", "晚班B", new TimeSpan(16, 0, 0), new TimeSpan(2, 0, 0), 1, 2, new List<long> { 8 })
        };

        // 需求(最少/最好)：16:00-20:00 = 1/1；20:00-22:00 = 2/3；22:00-24:00 = 1/2；00:00-04:00 = 1/1
        var staffing = new List<StaffingRequirementInput>();
        foreach (var slot in Enumerable.Range(0, 48).Select(i => TimeSpan.FromMinutes(i * 30)))
        {
            if (slot >= new TimeSpan(16, 0, 0) && slot < new TimeSpan(20, 0, 0))
            {
                staffing.Add(new StaffingRequirementInput("WORKDAY", 8, slot, 1, 1));
            }
            else if (slot >= new TimeSpan(20, 0, 0) && slot < new TimeSpan(22, 0, 0))
            {
                staffing.Add(new StaffingRequirementInput("WORKDAY", 8, slot, 2, 3));
            }
            else if (slot >= new TimeSpan(22, 0, 0))
            {
                staffing.Add(new StaffingRequirementInput("WORKDAY", 8, slot, 1, 2));
            }
            else if (slot < new TimeSpan(4, 0, 0))
            {
                staffing.Add(new StaffingRequirementInput("WORKDAY", 8, slot, 1, 1));
            }
        }

        return new SchedulingInput(
            1, D0, D1,
            employees, skills,
            new List<DateParameterInput>
            {
                new(D0, 2, "WORKDAY", 0, 0),
                new(D1, 3, "WORKDAY", 0, 0)
            },
            shiftTemplates, staffing,
            new List<ApprovedLeaveInput>(),
            new List<PeakRestrictedHourInput>(),
            new Dictionary<long, bool>(),
            new Dictionary<long, string>(),
            4, 6, 10, 0m);
    }

    [Fact]
    public void OverlappingShifts_NeverExceedPerSlotIdealCeiling()
    {
        var input = BuildInput();
        var engine = new SchedulingEngine(null!);

        var output = engine.Execute(input);

        // 需求字典(最好人数上限)
        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);
        var ideal = new Dictionary<(DateOnly, TimeSpan), int>();
        foreach (var r in input.StaffingRequirements.Where(r => r.DayType == "WORKDAY"))
        {
            foreach (var date in input.DateParameters.Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate))
            {
                // 周期首日凌晨跳过(与算法一致)
                if (r.TimeSlot < TimeSpan.FromHours(6) && date.WorkDate == D0)
                {
                    continue;
                }
                var key = (date.WorkDate, r.TimeSlot);
                ideal[key] = Math.Max(ideal.GetValueOrDefault(key), r.IdealCount > 0 ? r.IdealCount : r.RequiredCount);
            }
        }

        // 按 (日历日, 时段) 统计工作站分配人数
        var headcount = new Dictionary<(DateOnly, TimeSpan), int>();
        var shiftById = input.ShiftTemplates.ToDictionary(s => s.Id);
        foreach (var a in output.WorkstationAssignments)
        {
            // D 班次(模板 id 为负)在本用例中为同日缺口块(不跨午夜)，日历日 = WorkDate
            var calendarDate = shiftById.TryGetValue(a.ShiftTemplateId, out var template)
                ? SchedulingTimeHelper.SlotCalendarDate(a.TimeSlot, template.StartTime, a.WorkDate)
                : a.WorkDate;
            var key = (calendarDate, a.TimeSlot);
            headcount[key] = headcount.GetValueOrDefault(key) + 1;
        }

        foreach (var kv in headcount)
        {
            // 与算法一致：仅对「已配置正需求」的时段执行上限断言（未配置/0 需求时段不约束）
            if (!ideal.TryGetValue(kv.Key, out var limit) || limit <= 0)
            {
                continue;
            }

            Assert.True(kv.Value <= limit,
                $"日历日 {kv.Key.Item1:yyyy-MM-dd} {kv.Key.Item2:hh\\:mm} 上岗 {kv.Value} 人超过最好人数 {limit}");
        }

        // 晚间峰值(20:00-21:30 最少 2)应恰好满足：T1 的 1 人 + D 班次按块回填 1 人，不多排
        var eveningHeadcount = headcount
            .Where(kv => kv.Key.Item1 == D0 && kv.Key.Item2 >= new TimeSpan(20, 0, 0) && kv.Key.Item2 < new TimeSpan(21, 30, 0))
            .ToList();
        Assert.All(eveningHeadcount, kv => Assert.Equal(2, kv.Value));

        // 16:00-19:30(最少 1)由 D 班次回填；凌晨 00:00 由 T1 跨天尾巴覆盖，
        // 且归属【次日日历日 D1】(D0 自身凌晨属上一营业日、周期外，无人)
        Assert.Equal(1, headcount.GetValueOrDefault((D0, new TimeSpan(16, 0, 0))));
        Assert.Equal(1, headcount.GetValueOrDefault((D1, TimeSpan.Zero)));
    }
}
