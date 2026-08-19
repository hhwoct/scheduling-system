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

    /// <summary>需求键：(工作站, 日历日, 时段)。日历日 00:00-05:30 归属上一营业日。</summary>
    private readonly record struct DemandKey(long WorkstationId, DateOnly Date, TimeSpan Slot);

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

        // 营业日口径：某日历日 00:00-05:30 的凌晨时段归属上一营业日（用前一天类型取需求）。
        // 需求字典【全程持久】且键带日历日期：跨天班次的午夜回绕部分按次日日历键扣减，
        // 下一次迭代能看到本次的扣减，修复"凌晨需求匹配错营业日 + 扣减丢失"。
        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);
        var remaining = BuildDemandDictionary(input, dates, dayTypeByDate, ideal: false);
        var idealRequirements = BuildDemandDictionary(input, dates, dayTypeByDate, ideal: true);
        // 覆盖计数：(工作站, 日历日, 时段) -> 已排人数
        var coverage = new Dictionary<DemandKey, int>();

        // 员工占用时段（日历日, 时段）：防止当天新班次与前一天跨天班次的午夜回绕部分重叠（双排）
        var busySlotsByEmployee = new Dictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>>();

        var weeklyHours = new Dictionary<long, decimal>();
        // 整个排班周期内的累计工时（跨周不清零）。
        // 兼职员工按“周期累计工时从高到低”优先复用，用更少的兼职人员。
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

            var assignedToday = new HashSet<long>();

            // 按稀缺班次优先级分配
            var orderedShifts = input.ShiftTemplates
                .OrderBy(s => ShiftPriorityMap.TryGetValue(s.Code, out var p) ? p : int.MaxValue)
                .ThenBy(s => s.Priority)
                .ToList();

            foreach (var shift in orderedShifts)
            {
                var required = DailyRequiredForShift(shift, remaining, date.WorkDate);
                if (required <= 0)
                {
                    continue;
                }

                var candidates = workingEmployees
                    .Where(e => !assignedToday.Contains(e.Id))
                    .Where(e => !HasOverlap(busySlotsByEmployee, e.Id, shift, date.WorkDate))
                    .Where(e => weeklyHours.GetValueOrDefault(e.Id) < input.MaxWeeklyHours)
                    .Where(e => HasSkillForShift(e.Id, shift, skillsByEmployee))
                    .OrderBy(e => e.IsParttime)  // 全职优先，兼职靠后
                    .ThenByDescending(e => e.IsParttime == 1 ? periodHours.GetValueOrDefault(e.Id) : 0m)
                    .ThenByDescending(e => SkillCoverage(e.Id, shift, skillsByEmployee) * 100 + MaxSkillScore(e.Id, shift, skillsByEmployee))
                    .ThenBy(e => e.IsParttime == 1 ? 0m : weeklyHours.GetValueOrDefault(e.Id))
                    .ToList();

                var toAssign = Math.Min(required, candidates.Count);
                foreach (var employee in candidates.Take(toAssign))
                {
                    // 选站时即校验单时段人数上限：首选岗位被上限否决时会尝试其余有需求岗位
                    var targetWs = SelectWorkstation(employee.Id, shift, date.WorkDate, remaining, skillsByEmployee,
                        input.LowSkillWorkstationIds, coverage, idealRequirements);
                    if (targetWs is null)
                    {
                        continue;  // 无可分配(有需求且不超上限)的岗位时不占用人员
                    }

                    assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, shift.Id, shift.Code, targetWs));
                    assignedToday.Add(employee.Id);
                    MarkBusy(busySlotsByEmployee, employee.Id, shift, date.WorkDate);
                    var shiftHours = SchedulingTimeHelper.GetShiftHours(shift.StartTime, shift.EndTime, shift.IsCrossDay);
                    weeklyHours[employee.Id] = weeklyHours.GetValueOrDefault(employee.Id) + shiftHours;
                    periodHours[employee.Id] = periodHours.GetValueOrDefault(employee.Id) + shiftHours;

                    DecrementRemaining(shift, targetWs, date.WorkDate, remaining);
                    IncrementCoverage(shift, targetWs, date.WorkDate, coverage);
                }
            }

            // 剩余员工分配到需求缺口最大的班次。
            // 第一阶段：补足硬性缺口（最少人数）；第二阶段：补足软性缺口（最好人数）。
            var unassigned = workingEmployees
                .Where(e => !assignedToday.Contains(e.Id))
                .OrderBy(e => e.IsParttime)
                .ThenByDescending(e => e.IsParttime == 1 ? periodHours.GetValueOrDefault(e.Id) : 0m)
                .ThenBy(e => e.IsParttime == 1 ? 0m : weeklyHours.GetValueOrDefault(e.Id))
                .ToList();

            var softRemaining = ComputeSoftRemaining(idealRequirements, coverage, date.WorkDate);
            var outstandingDemand = WindowDemandSum(remaining, date.WorkDate);
            foreach (var employee in unassigned)
            {
                var hardPhase = outstandingDemand > 0;

                // 修复：周工时上限在硬性/软性阶段都必须遵守（硬性阶段不再突破上限），
                // 无法排班的缺口由 WorkstationAllocator 的 STAFFING_GAP 问题报告提示。
                if (weeklyHours.GetValueOrDefault(employee.Id) >= input.MaxWeeklyHours)
                {
                    continue;
                }

                var demand = hardPhase ? remaining : softRemaining;
                var demandSum = WindowDemandSum(demand, date.WorkDate);
                if (demandSum <= 0)
                {
                    break;  // 硬性/软性需求都已满足，不再安排多余员工
                }

                var bestShift = input.ShiftTemplates
                    .Where(s => ShiftHasOutstandingDemand(s, demand, date.WorkDate))
                    .Where(s => !HasOverlap(busySlotsByEmployee, employee.Id, s, date.WorkDate))
                    .Where(s => HasSkillForShift(employee.Id, s, skillsByEmployee))
                    .OrderByDescending(s => ShiftDemandScore(s, demand, date.WorkDate))
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

                // 选站时即校验单时段人数上限：首选岗位被上限否决时会尝试其余有需求岗位
                var targetWs = SelectWorkstation(employee.Id, bestShift, date.WorkDate, demand, skillsByEmployee,
                    input.LowSkillWorkstationIds, coverage, idealRequirements);
                if (targetWs is null)
                {
                    if (hardPhase)
                    {
                        continue;
                    }

                    break;
                }

                assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, bestShift.Id, bestShift.Code, targetWs));
                assignedToday.Add(employee.Id);
                MarkBusy(busySlotsByEmployee, employee.Id, bestShift, date.WorkDate);
                var bestShiftHours = SchedulingTimeHelper.GetShiftHours(bestShift.StartTime, bestShift.EndTime, bestShift.IsCrossDay);
                weeklyHours[employee.Id] = weeklyHours.GetValueOrDefault(employee.Id) + bestShiftHours;
                periodHours[employee.Id] = periodHours.GetValueOrDefault(employee.Id) + bestShiftHours;

                var beforeSum = demandSum;
                DecrementRemaining(bestShift, targetWs, date.WorkDate, demand);
                IncrementCoverage(bestShift, targetWs, date.WorkDate, coverage);

                if (hardPhase)
                {
                    outstandingDemand = WindowDemandSum(remaining, date.WorkDate);
                    if (outstandingDemand <= 0)
                    {
                        softRemaining = ComputeSoftRemaining(idealRequirements, coverage, date.WorkDate);
                    }
                    else if (outstandingDemand >= beforeSum)
                    {
                        break;  // 硬性缺口无法再减少
                    }
                }
                else
                {
                    softRemaining = ComputeSoftRemaining(idealRequirements, coverage, date.WorkDate);
                    var afterSum = WindowDemandSum(softRemaining, date.WorkDate);
                    if (afterSum <= 0 || afterSum >= beforeSum)
                    {
                        break;  // 软性缺口已清空或无法再减少
                    }
                }
            }

            // ========== 需求缺口自动补班次 ==========
            // 仅为【当天日历日】的缺口生成 D 班次；次日凌晨（date+1 <06:00）的缺口
            // 留到下一轮迭代按其当日处理（D 班次从 00:00 开始属于次日），避免班次日期错位。
            if (remaining.Any(kv => kv.Value > 0 && kv.Key.Date == date.WorkDate))
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
                    generatedTemplates,
                    busySlotsByEmployee,
                    idealRequirements);
            }
        }

        return assignments;
    }

    /// <summary>
    /// 按日历日构建需求字典：(工作站, 日历日, 时段) -> 人数。
    /// 某日历日 00:00-05:30 的时段归属上一营业日（用前一天日期类型取需求）。
    /// </summary>
    private static Dictionary<DemandKey, int> BuildDemandDictionary(
        SchedulingInput input,
        IReadOnlyList<DateParameterInput> dates,
        IReadOnlyDictionary<DateOnly, string> dayTypeByDate,
        bool ideal)
    {
        var result = new Dictionary<DemandKey, int>();
        var firstDate = dates.Count > 0 ? dates[0].WorkDate : input.StartDate;
        foreach (var date in dates)
        {
            var dayType = dayTypeByDate.GetValueOrDefault(date.WorkDate) ?? "WORKDAY";
            var prevType = dayTypeByDate.GetValueOrDefault(date.WorkDate.AddDays(-1)) ?? dayType;
            foreach (var r in input.StaffingRequirements)
            {
                // 周期首日的凌晨时段归属上一营业日（不在排班周期内），跳过避免幻影缺口
                if (r.TimeSlot < TimeSpan.FromHours(6) && date.WorkDate == firstDate)
                {
                    continue;
                }

                var expectedType = r.TimeSlot < TimeSpan.FromHours(6) ? prevType : dayType;
                if (r.DayType != expectedType)
                {
                    continue;
                }

                var value = ideal ? (r.IdealCount > 0 ? r.IdealCount : r.RequiredCount) : r.RequiredCount;
                if (value <= 0)
                {
                    continue;
                }

                var key = new DemandKey(r.WorkstationId, date.WorkDate, r.TimeSlot);
                result[key] = Math.Max(result.GetValueOrDefault(key), value);
            }
        }

        return result;
    }

    /// <summary>某班次覆盖的 (日历日, 时段) 序列：午夜回绕部分属于次日。</summary>
    private static IEnumerable<(DateOnly Date, TimeSpan Slot)> ShiftSlots(ShiftTemplateInput shift, DateOnly workDate)
        => SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay)
            .Select(slot => (SchedulingTimeHelper.SlotCalendarDate(slot, shift.StartTime, workDate), slot));

    /// <summary>记录员工新班次的全部占用时段（含跨天午夜回绕部分）。</summary>
    private static void MarkBusy(
        Dictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>> busySlotsByEmployee,
        long employeeId,
        ShiftTemplateInput shift,
        DateOnly workDate)
    {
        if (!busySlotsByEmployee.TryGetValue(employeeId, out var set))
        {
            set = new HashSet<(DateOnly, TimeSpan)>();
            busySlotsByEmployee[employeeId] = set;
        }

        foreach (var (date, slot) in ShiftSlots(shift, workDate))
        {
            set.Add((date, slot));
        }
    }

    /// <summary>新班次是否与员工已排班次重叠（防双排：前一天跨天班次的午夜回绕与次日凌晨班次冲突）。</summary>
    private static bool HasOverlap(
        IReadOnlyDictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>> busySlotsByEmployee,
        long employeeId,
        ShiftTemplateInput shift,
        DateOnly workDate)
        => busySlotsByEmployee.TryGetValue(employeeId, out var set)
           && ShiftSlots(shift, workDate).Any(p => set.Contains((p.Date, p.Slot)));

    /// <summary>
    /// 单时段人数上限校验：给员工分配某班次后，目标工作站在班次窗口内的任一
    /// 已配置正需求的（日历日, 时段）覆盖人数不得超过其「最好人数」上限。
    /// 未配置/0 需求的时段不约束（允许班次穿行）。
    /// 目的：防止整段班次按峰值配人与重叠班次叠加，导致单时段人数超过需求配置。
    /// </summary>
    private static bool ExceedsCeiling(
        ShiftTemplateInput shift,
        long workstationId,
        DateOnly workDate,
        IReadOnlyDictionary<DemandKey, int> coverage,
        IReadOnlyDictionary<DemandKey, int> ceiling)
    {
        foreach (var (date, slot) in ShiftSlots(shift, workDate))
        {
            var key = new DemandKey(workstationId, date, slot);
            if (ceiling.TryGetValue(key, out var limit) && coverage.GetValueOrDefault(key) + 1 > limit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>迭代日 d 的排班窗口：当日全部时段 + 次日凌晨（<06:00，跨天班次可覆盖）。</summary>
    private static bool InWindow(DateOnly keyDate, TimeSpan slot, DateOnly date)
        => keyDate == date || (keyDate == date.AddDays(1) && slot < TimeSpan.FromHours(6));

    private static int WindowDemandSum(
        IReadOnlyDictionary<DemandKey, int> demand,
        DateOnly date)
        => demand
            .Where(kv => InWindow(kv.Key.Date, kv.Key.Slot, date))
            .Sum(kv => kv.Value);

    // D 班次按工作站分块后块数增多（每站每缺口区间一块），上限放宽；
    // 每天总人数仍受需求与可用员工自然约束。
    private const int MaxDemandShiftBlocksPerDay = 32;

    private const int MaxDemandShiftHeadcount = 12;

    /// <summary>
    /// 模板班次无法覆盖的硬性缺口，按连续缺口块自动生成临时班次（D1、D2…），
    /// 用当天未排班的员工补齐；班次起止时间跟随需求块，可跨午夜。
    /// </summary>
    private static void GenerateDemandShifts(
        SchedulingInput input,
        DateOnly workDate,
        Dictionary<DemandKey, int> remaining,
        Dictionary<DemandKey, int> coverage,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyList<EmployeeInput> workingEmployees,
        HashSet<long> assignedToday,
        Dictionary<long, decimal> weeklyHours,
        Dictionary<long, decimal> periodHours,
        List<ShiftAssignment> assignments,
        List<ShiftTemplateInput> generatedTemplates,
        Dictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>> busySlotsByEmployee,
        IReadOnlyDictionary<DemandKey, int> idealRequirements)
    {
        // 临时班次可覆盖的工作站集合（用于技能校验与选站）
        var allWorkstationIds = input.StaffingRequirements
            .Select(r => r.WorkstationId)
            .Union(input.LowSkillWorkstationIds.Keys)
            .Distinct()
            .ToList();

        // 按【工作站】分组生成缺口块：每个 D 班次只跟随单个工作站自己的缺口区间。
        // 修复：原来按时段跨站合并成一个块，员工被塞进"别站有缺口的时段"时，
        // 其本站在该时段已满员（上限），整班被人数上限误拒，缺口无人可补。
        var blocksUsed = 0;
        foreach (var wsGroup in remaining
                     .Where(kv => kv.Value > 0 && kv.Key.Date == workDate)
                     .GroupBy(kv => kv.Key.WorkstationId)
                     .OrderBy(g => g.Key))
        {
            var workstationId = wsGroup.Key;
            var gapSlots = wsGroup
                .Select(kv => kv.Key.Slot)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            // 合并该工作站自己的连续缺口块（30 分钟相邻）
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

            foreach (var block in blocks)
            {
                if (blocksUsed >= MaxDemandShiftBlocksPerDay)
                {
                    return;
                }

                if (!BlockHasDemandForStation(block.Start, block.EndExclusive, workstationId, remaining, workDate))
                {
                    continue;
                }

                blocksUsed++;
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
                while (BlockHasDemandForStation(block.Start, block.EndExclusive, workstationId, remaining, workDate) && usedThisBlock < MaxDemandShiftHeadcount)
            {
                var orderedCandidates = workingEmployees
                    // 修复：不再用 assignedToday 排除当天已有班次的员工——只要 D 班次时段
                    // 与其已有班次不重叠（HasOverlap 校验）即可补位；否则"20:00 才上班的
                    // 楼面员工无法补 17:00-19:30 缺口"这类本可覆盖的缺口永远补不上。
                    .Where(e => !HasOverlap(busySlotsByEmployee, e.Id, template, workDate))
                    .Where(e => weeklyHours.GetValueOrDefault(e.Id) < input.MaxWeeklyHours)
                    .Where(e => HasSkillForShift(e.Id, template, skillsByEmployee))
                    .OrderBy(e => e.IsParttime)
                    .ThenByDescending(e => e.IsParttime == 1 ? periodHours.GetValueOrDefault(e.Id) : 0m)
                    .ThenByDescending(e => SkillCoverage(e.Id, template, skillsByEmployee) * 100 + MaxSkillScore(e.Id, template, skillsByEmployee))
                    .ThenBy(e => e.IsParttime == 1 ? 0m : weeklyHours.GetValueOrDefault(e.Id))
                    .ToList();

                // 选第一个「选得到工作站且不超过该时段最好人数上限」的候选人，避免整块因上限被放弃
                EmployeeInput? candidate = null;
                long? targetWs = null;
                foreach (var e in orderedCandidates)
                {
                    // 选站时即校验单时段人数上限：首选岗位被上限否决时会尝试其余有需求岗位
                    var ws = SelectWorkstation(e.Id, template, workDate, remaining, skillsByEmployee,
                        input.LowSkillWorkstationIds, coverage, idealRequirements);
                    if (ws is null)
                    {
                        continue;
                    }

                    candidate = e;
                    targetWs = ws;
                    break;
                }

                if (candidate is null || targetWs is null)
                {
                    break;
                }

                assignments.Add(new ShiftAssignment(candidate.Id, workDate, template.Id, template.Code, targetWs));
                assignedToday.Add(candidate.Id);
                MarkBusy(busySlotsByEmployee, candidate.Id, template, workDate);
                var hours = SchedulingTimeHelper.GetShiftHours(template.StartTime, template.EndTime, template.IsCrossDay);
                weeklyHours[candidate.Id] = weeklyHours.GetValueOrDefault(candidate.Id) + hours;
                periodHours[candidate.Id] = periodHours.GetValueOrDefault(candidate.Id) + hours;
                DecrementRemaining(template, targetWs, workDate, remaining);
                IncrementCoverage(template, targetWs, workDate, coverage);
                usedThisBlock++;
                }
            }
        }
    }

    private static TimeSpan EndOf(TimeSpan lastSlot)
    {
        var end = lastSlot.Add(TimeSpan.FromMinutes(30));
        return end >= TimeSpan.FromHours(24) ? end - TimeSpan.FromHours(24) : end;
    }

    private static bool BlockHasDemandForStation(
        TimeSpan start,
        TimeSpan endExclusive,
        long workstationId,
        IReadOnlyDictionary<DemandKey, int> remaining,
        DateOnly workDate)
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(start, endExclusive, endExclusive <= start ? 1 : 0);
        return remaining.Any(kv => kv.Value > 0 &&
                                   kv.Key.Date == workDate &&
                                   kv.Key.WorkstationId == workstationId &&
                                   slots.Contains(kv.Key.Slot));
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

    /// <summary>计算软性缺口：(工作站, 日历日, 时段) -> 最好人数 - 已排人数（仅 > 0 的保留）。</summary>
    private static Dictionary<DemandKey, int> ComputeSoftRemaining(
        IReadOnlyDictionary<DemandKey, int> idealRequirements,
        IReadOnlyDictionary<DemandKey, int> coverage,
        DateOnly date)
    {
        var soft = new Dictionary<DemandKey, int>();
        foreach (var req in idealRequirements)
        {
            if (!InWindow(req.Key.Date, req.Key.Slot, date))
            {
                continue;
            }

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
        DateOnly workDate,
        IDictionary<DemandKey, int> coverage)
    {
        if (workstationId is null)
        {
            return;
        }

        foreach (var (date, slot) in ShiftSlots(shift, workDate))
        {
            var key = new DemandKey(workstationId.Value, date, slot);
            coverage.TryGetValue(key, out var current);
            coverage[key] = current + 1;
        }
    }

    /// <summary>
    /// 班次在某工作站的并发需求 = 班次覆盖的所有时段中该工作站需求的最大值。
    /// </summary>
    private static int ConcurrentForWorkstation(
        ShiftTemplateInput shift,
        long workstationId,
        IReadOnlyDictionary<DemandKey, int> dayRequirements,
        DateOnly workDate)
    {
        var max = 0;
        foreach (var (date, slot) in ShiftSlots(shift, workDate))
        {
            max = Math.Max(max, dayRequirements.GetValueOrDefault(new DemandKey(workstationId, date, slot)));
        }
        return max;
    }

    /// <summary>该班次覆盖的时段内是否仍有未被满足的需求缺口。</summary>
    private static bool ShiftHasOutstandingDemand(
        ShiftTemplateInput shift,
        IReadOnlyDictionary<DemandKey, int> remaining,
        DateOnly workDate)
    {
        return shift.WorkstationIds.Any(ws => ShiftSlots(shift, workDate)
            .Any(p => remaining.GetValueOrDefault(new DemandKey(ws, p.Date, p.Slot)) > 0));
    }

    /// <summary>班次总需求 = 该班次覆盖的所有工作站的并发需求之和。</summary>
    private static int DailyRequiredForShift(
        ShiftTemplateInput shift,
        IReadOnlyDictionary<DemandKey, int> dayRequirements,
        DateOnly workDate)
    {
        return shift.WorkstationIds.Sum(ws => ConcurrentForWorkstation(shift, ws, dayRequirements, workDate));
    }

    private static int ShiftDemandScore(
        ShiftTemplateInput shift,
        IReadOnlyDictionary<DemandKey, int> dayRequirements,
        DateOnly workDate)
    {
        return shift.WorkstationIds.Sum(ws => ConcurrentForWorkstation(shift, ws, dayRequirements, workDate));
    }

    /// <summary>分配 1 人覆盖班次全部时段后，该工作站在这些时段的剩余需求各减 1。</summary>
    private static void DecrementRemaining(
        ShiftTemplateInput shift,
        long? workstationId,
        DateOnly workDate,
        IDictionary<DemandKey, int> remaining)
    {
        if (workstationId is null)
        {
            return;
        }

        foreach (var (date, slot) in ShiftSlots(shift, workDate))
        {
            var key = new DemandKey(workstationId.Value, date, slot);
            if (remaining.TryGetValue(key, out var cnt) && cnt > 0)
            {
                remaining[key] = cnt - 1;
            }
        }
    }

    /// <summary>
    /// <summary>
    /// 为员工选择有剩余需求的工作站：优先技能分最高的，且必须满足单时段人数上限
    /// （不超过「最好人数」）。首个被上限否决的岗位不再直接丢弃员工，
    /// 而是按技能顺序尝试班次覆盖的其余有需求岗位。
    /// 不允许把员工放到无技能的岗位——有技能时只在技能匹配的候选中选；
    /// 无技能匹配时仅允许低技能岗位兜底；无可分配岗位时返回 null（跳过该员工）。
    /// </summary>
    private static long? SelectWorkstation(
        long employeeId,
        ShiftTemplateInput shift,
        DateOnly workDate,
        IReadOnlyDictionary<DemandKey, int> remaining,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, bool> lowSkillWorkstationIds,
        IReadOnlyDictionary<DemandKey, int> coverage,
        IReadOnlyDictionary<DemandKey, int> ceiling)
    {
        var slotKeys = ShiftSlots(shift, workDate).Select(x => (x.Date, x.Slot)).ToList();

        var candidates = shift.WorkstationIds
            .Where(ws => slotKeys.Any(s => remaining.GetValueOrDefault(new DemandKey(ws, s.Date, s.Slot)) > 0))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var hasSkills = skillsByEmployee.TryGetValue(employeeId, out var skills) && skills.Count > 0;
        if (!hasSkills)
        {
            // 无任何技能的员工只允许低技能岗位。
            // 注意：必须用 Select((long?)ws).FirstOrDefault() 转可空，
            // 否则 List<long> 的 FirstOrDefault 会返回 0（非 null），产生"工作站 0"的假分配。
            return candidates
                .Where(ws => lowSkillWorkstationIds.ContainsKey(ws) && !ExceedsCeiling(shift, ws, workDate, coverage, ceiling))
                .Select(ws => (long?)ws)
                .FirstOrDefault();
        }

        // 技能匹配的岗位按技能分降序逐个尝试，取第一个不超过人数上限的
        foreach (var ws in candidates.Where(ws => skills!.ContainsKey(ws))
                     .OrderByDescending(ws => skills![ws])
                     .ThenBy(ws => ws))
        {
            if (!ExceedsCeiling(shift, ws, workDate, coverage, ceiling))
            {
                return ws;
            }
        }

        // 有技能但均不匹配有需求的岗位：仅低技能岗位可兜底
        // （同样注意 FirstOrDefault 返回 0 的问题，转成可空后再取）
        return candidates
            .Where(ws => lowSkillWorkstationIds.ContainsKey(ws) && !ExceedsCeiling(shift, ws, workDate, coverage, ceiling))
            .Select(ws => (long?)ws)
            .FirstOrDefault();
    }
}
