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
        IReadOnlyList<RestDayAssignment> restDays)
    {
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

        var weeklyHours = new Dictionary<long, decimal>();
        foreach (var employee in input.Employees)
        {
            weeklyHours[employee.Id] = 0m;
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
            var dayRequirements = CalculateDayRequirements(input, date);
            var remaining = dayRequirements.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
                    .OrderByDescending(e => SkillCoverage(e.Id, shift, skillsByEmployee) * 100 + MaxSkillScore(e.Id, shift, skillsByEmployee))
                    .ThenBy(e => weeklyHours.GetValueOrDefault(e.Id))
                    .ToList();

                var toAssign = Math.Min(required, candidates.Count);
                foreach (var employee in candidates.Take(toAssign))
                {
                    var targetWs = SelectWorkstation(employee.Id, shift, remaining, skillsByEmployee);
                    assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, shift.Id, shift.Code, targetWs));
                    assignedToday.Add(employee.Id);
                    weeklyHours[employee.Id] =
                        weeklyHours.GetValueOrDefault(employee.Id) + SchedulingTimeHelper.GetShiftHours(shift.StartTime, shift.EndTime, shift.IsCrossDay);

                    // 分配 1 人覆盖整个班次时段：该工作站在班次覆盖的所有时段剩余需求减 1
                    DecrementRemaining(shift, targetWs, remaining);
                }
            }

            // 剩余员工分配到需求缺口最大的班次。
            // 修复：仅当当天仍有需求缺口时才补充人员；需求已满足后，
            // 多余员工（如兼职）当天不排班（空闲），避免超配排班。
            var unassigned = workingEmployees.Where(e => !assignedToday.Contains(e.Id)).ToList();
            var outstandingDemand = remaining.Values.Sum();
            foreach (var employee in unassigned)
            {
                if (outstandingDemand <= 0)
                    break;   // 当天需求已全部满足，不再安排多余员工

                var bestShift = input.ShiftTemplates
                    .Where(s => ShiftHasOutstandingDemand(s, remaining))
                    .Where(s => HasSkillForShift(employee.Id, s, skillsByEmployee))
                    .OrderByDescending(s => ShiftDemandScore(s, remaining))
                    .ThenBy(s => SchedulingTimeHelper.GetShiftHours(s.StartTime, s.EndTime, s.IsCrossDay))
                    .FirstOrDefault();

                if (bestShift is not null)
                {
                    var targetWs = SelectWorkstation(employee.Id, bestShift, remaining, skillsByEmployee);
                    assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, bestShift.Id, bestShift.Code, targetWs));
                    assignedToday.Add(employee.Id);
                    weeklyHours[employee.Id] =
                        weeklyHours.GetValueOrDefault(employee.Id) + SchedulingTimeHelper.GetShiftHours(bestShift.StartTime, bestShift.EndTime, bestShift.IsCrossDay);

                    var before = outstandingDemand;
                    DecrementRemaining(bestShift, targetWs, remaining);
                    outstandingDemand = remaining.Values.Sum();
                    if (outstandingDemand <= 0 || outstandingDemand >= before)
                        break;  // 不再有缺口（或无法再减少）则停止补充
                }
            }
        }

        return assignments;
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
        DateParameterInput date)
    {
        var dayType = date.DayType;
        return input.StaffingRequirements
            .Where(r => r.DayType == dayType)
            .GroupBy(r => (r.WorkstationId, r.TimeSlot))
            .ToDictionary(g => g.Key, g => g.Max(r => r.RequiredCount));
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