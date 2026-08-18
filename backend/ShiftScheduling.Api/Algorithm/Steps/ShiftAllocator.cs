namespace ShiftScheduling.Api.Algorithm.Steps;

public sealed class ShiftAllocator
{
    private static readonly Dictionary<string, int> ShiftPriorityMap = new()
    {
        ["S7"] = 1,
        ["S8"] = 2,
        ["S2"] = 3,
        ["S3"] = 4,
        ["S1"] = 5,
        ["S6"] = 6,
        ["S4"] = 7,
        ["S5"] = 8
    };

    public IReadOnlyList<ShiftAssignment> Allocate(
        SchedulingInput input,
        IReadOnlyList<RestDayAssignment> restDays,
        List<ShiftTemplateInput>? generatedTemplates = null)
    {
        generatedTemplates ??= new List<ShiftTemplateInput>();
        var assignments = new List<ShiftAssignment>();
        var restSet = restDays
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.WorkDate).ToHashSet());

        var skillsByEmployee = input.Skills
            .Where(x => x.SkillScore > 0)
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.WorkstationId, s => s.SkillScore));

        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();

        // 营业日口径：12:00 起至次日 06:00 为一个营业日，凌晨时段（00:00-05:30）按前一天的类型取需求
        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);

        var weeklyHours = new Dictionary<long, decimal>();
        // 整个排班周期内的累计工时（跨周不清零）。
        // 兼职员工按“周期累计工时从高到低”优先复用：把兼职需求尽量集中到
        // 已在用的少数兼职人员身上（例如一二三四五六都用兼传送A，而不是
        // 一二三用A、四五六用B），从而用更少的兼职人员。
        var periodHours = new Dictionary<long, decimal>();
        foreach (var employee in input.Employees)
        {
            weeklyHours[employee.Id] = 0m;
            periodHours[employee.Id] = 0m;
        }

        foreach (var date in dates)
        {
            // P1-5 修复3：周工时在每周周一边界重置
            if (date.WeekDay == 1)
            {
                foreach (var key in weeklyHours.Keys.ToList())
                {
                    weeklyHours[key] = 0m;
                }
            }

            var workingEmployees = input.Employees
                .Where(e => !restSet.TryGetValue(e.Id, out var rest) || !rest.Contains(date.WorkDate))
                .ToList();

            // ==========================================================
            // 修复：基于“班次覆盖时段内的并发需求峰值”计算需求，
            // 而不是把全天所有时段的需求简单累加。
            // 例如 KITCHEN：18:00-23:30 每槽 2 人 + 0:00-3:00 每槽 1 人，
            // 某班次覆盖这些时段时并发峰值 = max(2, 1) = 2 人（HOLIDAY 3）。
            // ==========================================================
            var prevType = dayTypeByDate.GetValueOrDefault(date.WorkDate.AddDays(-1)) ?? date.DayType;
            var dayRequirements = CalculateDayRequirements(input, date.DayType, prevType);
            var remaining = dayRequirements.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            // 最好人数（软性目标）：硬性需求满足后，用剩余员工尽量补足至理想人数
            var idealRequirements = CalculateIdealRequirements(input, date.DayType, prevType);
            // 覆盖计数：(工作站, 时段) -> 已排人数（用于计算软性缺口）
            var coverage = new Dictionary<(long WorkstationId, TimeSpan Slot), int>();
            var assignedToday = new HashSet<long>();

            // 按稀缺班次优先级分配
            var orderedShifts = input.ShiftTemplates
                .OrderBy(s => ShiftPriorityMap.TryGetValue(s.Code, out var p) ? p : int.MaxValue)
                .ThenBy(s => s.Priority)
                .ToList();

            foreach (var shift in orderedShifts)
            {
                var required = DailyRequiredForShift(shift, remaining);
                if (required <= 0)
                {
                    continue;
                }

                var candidates = workingEmployees
                    .Where(e => !assignedToday.Contains(e.Id))
                    .Where(e => weeklyHours.GetValueOrDefault(e.Id) < input.MaxWeeklyHours)
                    .Where(e => HasSkillForShift(e.Id, shift, skillsByEmployee))
                    .OrderBy(e => e.IsParttime)  // 全职优先，兼职靠后
                    // 兼职：周期累计工时高者优先 → 复用已在用的兼职，少用兼职人员；
                    // 全职：该键恒为 0，不影响全职原有排序。
                    .ThenByDescending(e => e.IsParttime == 1 ? periodHours.GetValueOrDefault(e.Id) : 0m)
                    .ThenByDescending(e => SkillCoverage(e.Id, shift, skillsByEmployee) * 100 + MaxSkillScore(e.Id, shift, skillsByEmployee))
                    // 全职：周工时低者优先（均衡）；兼职：该键恒为 0，不再按工时均衡。
                    .ThenBy(e => e.IsParttime == 1 ? 0m : weeklyHours.GetValueOrDefault(e.Id))
                    .ToList();

                var toAssign = Math.Min(required, candidates.Count);
                foreach (var employee in candidates.Take(toAssign))
                {
                    var targetWs = SelectWorkstation(employee.Id, shift, remaining, skillsByEmployee);
                    assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, shift.Id, shift.Code, targetWs));
                    assignedToday.Add(employee.Id);
                    var shiftHours = SchedulingTimeHelper.GetShiftHours(shift.StartTime, shift.EndTime, shift.IsCrossDay);
                    weeklyHours[employee.Id] = weeklyHours.GetValueOrDefault(employee.Id) + shiftHours;
                    periodHours[employee.Id] = periodHours.GetValueOrDefault(employee.Id) + shiftHours;

                    // 分配 1 人覆盖整个班次时段：该工作站在班次覆盖的所有时段剩余需求减 1
                    DecrementRemaining(shift, targetWs, remaining);
                    IncrementCoverage(shift, targetWs, coverage);
                }
            }

            // 剩余员工分配到需求缺口最大的班次。
            // 第一阶段：补足硬性缺口（最少人数）；第二阶段：硬性满足后，
            // 用剩余员工尽量补足软性缺口（最好人数），补足不了也不影响排班。
            var unassigned = workingEmployees
                .Where(e => !assignedToday.Contains(e.Id))
                .OrderBy(e => e.IsParttime)  // 全职优先，兼职靠后
                // 兼职：周期累计工时高者优先 → 复用已在用的兼职，少用兼职人员；
                // 全职：该键恒为 0。
                .ThenByDescending(e => e.IsParttime == 1 ? periodHours.GetValueOrDefault(e.Id) : 0m)
                // 全职：周工时低者优先（均衡）；兼职：该键恒为 0。
                .ThenBy(e => e.IsParttime == 1 ? 0m : weeklyHours.GetValueOrDefault(e.Id))
                .ToList();
            var outstandingDemand = remaining.Values.Sum();
            var softRemaining = ComputeSoftRemaining(idealRequirements, coverage);
            foreach (var employee in unassigned)
            {
                var hardPhase = outstandingDemand > 0;
                if (!hardPhase && weeklyHours.GetValueOrDefault(employee.Id) >= input.MaxWeeklyHours)
                {
                    continue;  // 软性补足阶段不再给已达周工时上限的员工排班
                }

                var demand = hardPhase ? remaining : softRemaining;
                var demandSum = demand.Values.Sum();
                if (demandSum <= 0)
                {
                    break;  // 硬性/软性需求都已满足，不再安排多余员工
                }

                var bestShift = input.ShiftTemplates
                    .Where(s => ShiftHasOutstandingDemand(s, demand))
                    .Where(s => HasSkillForShift(employee.Id, s, skillsByEmployee))
                    .OrderByDescending(s => ShiftDemandScore(s, demand))
                    .ThenBy(s => SchedulingTimeHelper.GetShiftHours(s.StartTime, s.EndTime, s.IsCrossDay))
                    .FirstOrDefault();

                if (bestShift is null)
                {
                    if (hardPhase)
                    {
                        continue;  // 硬性阶段该员工无可排班次，尝试下一个员工
                    }

                    break;  // 软性阶段无合适班次，停止补充
                }

                var targetWs = SelectWorkstation(employee.Id, bestShift, demand, skillsByEmployee);
                assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, bestShift.Id, bestShift.Code, targetWs));
                assignedToday.Add(employee.Id);
                var bestShiftHours = SchedulingTimeHelper.GetShiftHours(bestShift.StartTime, bestShift.EndTime, bestShift.IsCrossDay);
                weeklyHours[employee.Id] = weeklyHours.GetValueOrDefault(employee.Id) + bestShiftHours;
                periodHours[employee.Id] = periodHours.GetValueOrDefault(employee.Id) + bestShiftHours;

                var beforeSum = demandSum;
                DecrementRemaining(bestShift, targetWs, demand);
                IncrementCoverage(bestShift, targetWs, coverage);

                if (hardPhase)
                {
                    outstandingDemand = remaining.Values.Sum();
                    if (outstandingDemand <= 0)
                    {
                        softRemaining = ComputeSoftRemaining(idealRequirements, coverage);
                    }
                    else if (outstandingDemand >= beforeSum)
                    {
                        break;  // 硬性缺口无法再减少
                    }
                }
                else
                {
                    softRemaining = ComputeSoftRemaining(idealRequirements, coverage);
                    var afterSum = softRemaining.Values.Sum();
                    if (afterSum <= 0 || afterSum >= beforeSum)
                    {
                        break;  // 软性缺口已清空或无法再减少
                    }
                }
            }
            // ========== 需求缺口自动补班次 ==========
            // 模板班次排完后仍有硬性缺口（最少人数未满足）的时段，按连续缺口块
            // 生成临时班次（D1、D2…），起止时间跟随需求曲线，可跨午夜。
            if (remaining.Values.Sum() > 0)
            {
                GenerateDemandShifts(
                    input,
                    date.WorkDate,
                    remaining,
                    coverage,
                    skillsByEmployee,
                    workingEmployees,
                    assignedToday,
                    weeklyHours,
                    periodHours,
                    assignments,
                    generatedTemplates);
            }
        }

        return assignments;
    }

    private const int MaxDemandShiftBlocksPerDay = 8;

    private const int MaxDemandShiftHeadcount = 12;

    /// <summary>
    /// 模板班次无法覆盖的硬性缺口，按连续缺口块自动生成临时班次（D1、D2…），
    /// 用当天未排班的员工补齐；班次起止时间跟随需求块，可跨午夜。
    /// </summary>
    private static void GenerateDemandShifts(
        SchedulingInput input,
        DateOnly workDate,
        Dictionary<(long WorkstationId, TimeSpan Slot), int> remaining,
        Dictionary<(long WorkstationId, TimeSpan Slot), int> coverage,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyList<EmployeeInput> workingEmployees,
        HashSet<long> assignedToday,
        Dictionary<long, decimal> weeklyHours,
        Dictionary<long, decimal> periodHours,
        List<ShiftAssignment> assignments,
        List<ShiftTemplateInput> generatedTemplates)
    {
        var gapSlots = remaining
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key.Slot)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (gapSlots.Count == 0)
        {
            return;
        }

        // 合并连续缺口块（30 分钟相邻，含跨午夜回绕）
        var blocks = new List<(TimeSpan Start, TimeSpan EndExclusive)>();
        var blockStart = gapSlots[0];
        var prev = gapSlots[0];
        for (var i = 1; i < gapSlots.Count; i++)
        {
            var nextOfPrev = prev.Add(TimeSpan.FromMinutes(30));
            if (nextOfPrev >= TimeSpan.FromHours(24))
            {
                nextOfPrev -= TimeSpan.FromHours(24);
            }

            if (gapSlots[i] == nextOfPrev)
            {
                prev = gapSlots[i];
                continue;
            }

            blocks.Add((blockStart, EndOf(prev)));
            blockStart = gapSlots[i];
            prev = gapSlots[i];
        }

        blocks.Add((blockStart, EndOf(prev)));

        // 临时班次可覆盖的工作站集合（用于技能校验与选站）
        var allWorkstationIds = input.StaffingRequirements
            .Select(r => r.WorkstationId)
            .Union(input.LowSkillWorkstationIds.Keys)
            .Distinct()
            .ToList();

        foreach (var block in blocks.Take(MaxDemandShiftBlocksPerDay))
        {
            if (!BlockHasDemand(block.Start, block.EndExclusive, remaining))
            {
                continue;
            }

            var n = generatedTemplates.Count + 1;
            var code = "D" + n;
            var isCrossDay = block.EndExclusive <= block.Start ? 1 : 0;
            var template = new ShiftTemplateInput(
                -1000 - n,
                code,
                "按需求补班",
                block.Start,
                block.EndExclusive,
                isCrossDay,
                1,
                allWorkstationIds);
            generatedTemplates.Add(template);

            var usedThisBlock = 0;
            while (BlockHasDemand(block.Start, block.EndExclusive, remaining) && usedThisBlock < MaxDemandShiftHeadcount)
            {
                var candidate = workingEmployees
                    .Where(e => !assignedToday.Contains(e.Id))
                    .Where(e => weeklyHours.GetValueOrDefault(e.Id) < input.MaxWeeklyHours)
                    .Where(e => HasSkillForShift(e.Id, template, skillsByEmployee))
                    .OrderBy(e => e.IsParttime)
                    .ThenByDescending(e => e.IsParttime == 1 ? periodHours.GetValueOrDefault(e.Id) : 0m)
                    .ThenByDescending(e => SkillCoverage(e.Id, template, skillsByEmployee) * 100 + MaxSkillScore(e.Id, template, skillsByEmployee))
                    .ThenBy(e => e.IsParttime == 1 ? 0m : weeklyHours.GetValueOrDefault(e.Id))
                    .FirstOrDefault();

                if (candidate is null)
                {
                    break;
                }

                var targetWs = SelectWorkstation(candidate.Id, template, remaining, skillsByEmployee);
                assignments.Add(new ShiftAssignment(candidate.Id, workDate, template.Id, template.Code, targetWs));
                assignedToday.Add(candidate.Id);
                var hours = SchedulingTimeHelper.GetShiftHours(template.StartTime, template.EndTime, template.IsCrossDay);
                weeklyHours[candidate.Id] = weeklyHours.GetValueOrDefault(candidate.Id) + hours;
                periodHours[candidate.Id] = periodHours.GetValueOrDefault(candidate.Id) + hours;
                DecrementRemaining(template, targetWs, remaining);
                IncrementCoverage(template, targetWs, coverage);
                usedThisBlock++;
            }
        }
    }

    private static TimeSpan EndOf(TimeSpan lastSlot)
    {
        var end = lastSlot.Add(TimeSpan.FromMinutes(30));
        return end >= TimeSpan.FromHours(24) ? end - TimeSpan.FromHours(24) : end;
    }

    private static bool BlockHasDemand(
        TimeSpan start,
        TimeSpan endExclusive,
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> remaining)
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(start, endExclusive, endExclusive <= start ? 1 : 0);
        return remaining.Any(kv => kv.Value > 0 && slots.Contains(kv.Key.Slot));
    }

    private static bool HasSkillForShift(
        long employeeId,
        ShiftTemplateInput shift,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        if (!skillsByEmployee.TryGetValue(employeeId, out var skills))
        {
            return false;
        }

        return shift.WorkstationIds.Any(ws => skills.ContainsKey(ws));
    }

    private static int SkillCoverage(
        long employeeId,
        ShiftTemplateInput shift,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        if (!skillsByEmployee.TryGetValue(employeeId, out var skills))
        {
            return 0;
        }

        return shift.WorkstationIds.Count(ws => skills.ContainsKey(ws));
    }

    private static int MaxSkillScore(
        long employeeId,
        ShiftTemplateInput shift,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        if (!skillsByEmployee.TryGetValue(employeeId, out var skills))
        {
            return 0;
        }

        return shift.WorkstationIds.Where(skills.ContainsKey).Max(ws => skills[ws]);
    }

    /// <summary>
    /// 需求键：工作站 + 具体时段。值 = 该时段该工作站的并发需求人数。
    /// 不再按工作站对全天所有时段做 Sum 累加。
    /// </summary>
    private static IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> CalculateDayRequirements(
        SchedulingInput input,
        string dayType,
        string prevType)
    {
        // 凌晨时段（< 06:00）按前一天的日期类型取需求，其余按当天
        return input.StaffingRequirements
            .Where(r => r.DayType == (r.TimeSlot < TimeSpan.FromHours(6) ? prevType : dayType))
            .GroupBy(r => (r.WorkstationId, r.TimeSlot))
            .ToDictionary(g => g.Key, g => g.Max(r => r.RequiredCount));
    }

    /// <summary>
    /// 最好人数需求键：工作站 + 具体时段。值 = 该时段该工作站的最好人数（软性目标）。
    /// </summary>
    private static IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> CalculateIdealRequirements(
        SchedulingInput input,
        string dayType,
        string prevType)
    {
        // 凌晨时段（< 06:00）按前一天的日期类型取需求，其余按当天
        return input.StaffingRequirements
            .Where(r => r.DayType == (r.TimeSlot < TimeSpan.FromHours(6) ? prevType : dayType))
            .GroupBy(r => (r.WorkstationId, r.TimeSlot))
            .ToDictionary(
                g => g.Key,
                g => g.Max(r => r.IdealCount > 0 ? r.IdealCount : r.RequiredCount));
    }

    /// <summary>计算软性缺口：(工作站, 时段) -> 最好人数 - 已排人数（仅 > 0 的保留）。</summary>
    private static Dictionary<(long WorkstationId, TimeSpan Slot), int> ComputeSoftRemaining(
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> idealRequirements,
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> coverage)
    {
        var soft = new Dictionary<(long WorkstationId, TimeSpan Slot), int>();
        foreach (var req in idealRequirements)
        {
            var residual = req.Value - coverage.GetValueOrDefault(req.Key);
            if (residual > 0)
            {
                soft[req.Key] = residual;
            }
        }

        return soft;
    }

    /// <summary>某员工覆盖某班次时段后，该工作站在这些时段的覆盖人数各 +1。</summary>
    private static void IncrementCoverage(
        ShiftTemplateInput shift,
        long? workstationId,
        IDictionary<(long WorkstationId, TimeSpan Slot), int> coverage)
    {
        if (workstationId is null)
        {
            return;
        }

        var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);
        foreach (var slot in slots)
        {
            var key = (workstationId.Value, slot);
            coverage.TryGetValue(key, out var current);
            coverage[key] = current + 1;
        }
    }

    /// <summary>
    /// 班次在某工作站的并发需求 = 班次覆盖的所有时段中该工作站需求的最大值。
    /// 例如 S7 覆盖 22:00-23:30 + 0:00-3:00，KITCHEN 在上述时段需求峰值 = 2（HOLIDAY 3）。
    /// </summary>
    private static int ConcurrentForWorkstation(
        ShiftTemplateInput shift,
        long workstationId,
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> dayRequirements)
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);
        var max = 0;
        foreach (var slot in slots)
        {
            max = Math.Max(max, dayRequirements.GetValueOrDefault((workstationId, slot)));
        }
        return max;
    }

    /// <summary>
    /// 该班次覆盖的时段内是否仍有未被满足的需求缺口。
    /// remaining 键 = (工作站, 时段)。
    /// </summary>
    private static bool ShiftHasOutstandingDemand(
        ShiftTemplateInput shift,
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> remaining)
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);
        return shift.WorkstationIds.Any(ws => slots.Any(slot => remaining.GetValueOrDefault((ws, slot)) > 0));
    }

    /// <summary>
    /// 班次总需求 = 该班次覆盖的所有工作站的并发需求之和。
    /// 一个班次同一天需要的人数 = 各工作站覆盖时段峰值之和。
    /// </summary>
    private static int DailyRequiredForShift(
        ShiftTemplateInput shift,
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> dayRequirements)
    {
        return shift.WorkstationIds.Sum(ws => ConcurrentForWorkstation(shift, ws, dayRequirements));
    }

    private static int ShiftDemandScore(
        ShiftTemplateInput shift,
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> dayRequirements)
    {
        return shift.WorkstationIds.Sum(ws => ConcurrentForWorkstation(shift, ws, dayRequirements));
    }

    /// <summary>
    /// 分配 1 人覆盖班次全部时段后，该工作站在这些时段的剩余需求各减 1。
    /// </summary>
    private static void DecrementRemaining(
        ShiftTemplateInput shift,
        long? workstationId,
        IDictionary<(long WorkstationId, TimeSpan Slot), int> remaining)
    {
        if (workstationId is null)
        {
            return;
        }

        var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);
        foreach (var slot in slots)
        {
            var key = (workstationId.Value, slot);
            if (remaining.TryGetValue(key, out var cnt) && cnt > 0)
            {
                remaining[key] = cnt - 1;
            }
        }
    }

    /// <summary>
    /// 为员工选择剩余需求最高的工作站，优先员工技能分最高的。
    /// </summary>
    private static long? SelectWorkstation(
        long employeeId,
        ShiftTemplateInput shift,
        IReadOnlyDictionary<(long WorkstationId, TimeSpan Slot), int> remaining,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);

        var candidates = shift.WorkstationIds
            .Where(ws => slots.Any(slot => remaining.GetValueOrDefault((ws, slot)) > 0))
            .ToList();

        if (candidates.Count == 0)
        {
            return shift.WorkstationIds.FirstOrDefault();
        }

        if (!skillsByEmployee.TryGetValue(employeeId, out var skills))
        {
            return candidates.FirstOrDefault();
        }

        return candidates
            .OrderByDescending(ws => skills.GetValueOrDefault(ws))
            .ThenBy(ws => ws)
            .FirstOrDefault();
    }
}