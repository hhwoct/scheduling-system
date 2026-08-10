namespace ShiftScheduling.Api.Algorithm.Steps;

/// <summary>
/// 阶段三：连续性优先工作站分配。
/// 按需求文档 7.5 实现完整步骤：
/// 2A 逐段贪心参考分配 → 2B 确定主工作站 → 2C 初始连续分配 →
/// 2D 检测连续缺口块 → 2E 块状借调 → 2F 二次连续块填补 → 2G 最终逐段兜底
/// </summary>
public sealed class WorkstationAllocator
{
    private const int MaxBorrowPerEmployeePerDay = 2;

    public IReadOnlyList<WorkstationAssignment> Allocate(
        SchedulingInput input,
        IReadOnlyList<RestDayAssignment> restDays,
        IReadOnlyList<ShiftAssignment> shiftAssignments,
        List<ScheduleIssueOutput>? issueCollector = null)
    {
        var assignments = new List<WorkstationAssignment>();

        var skillsByEmployee = input.Skills
            .Where(x => x.SkillScore > 0)
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.WorkstationId, s => s.SkillScore));

        var shiftById = input.ShiftTemplates.ToDictionary(s => s.Id);

        var shiftsByDate = shiftAssignments
            .GroupBy(x => x.WorkDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();

        foreach (var date in dates)
        {
            if (!shiftsByDate.TryGetValue(date.WorkDate, out var dayShifts))
            {
                continue;
            }

            var dayResult = AllocateForDate(input, date, dayShifts, skillsByEmployee, shiftById);
            assignments.AddRange(dayResult.Assignments);
            issueCollector?.AddRange(dayResult.Issues);
        }

        return assignments;
    }

    private static (IReadOnlyList<WorkstationAssignment> Assignments, IReadOnlyList<ScheduleIssueOutput> Issues) AllocateForDate(
        SchedulingInput input,
        DateParameterInput date,
        IReadOnlyList<ShiftAssignment> dayShifts,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById)
    {
        var dayType = date.DayType;
        var requirements = input.StaffingRequirements
            .Where(r => r.DayType == dayType)
            .GroupBy(r => r.TimeSlot)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.WorkstationId, r => r.RequiredCount));

        var assignments = new List<WorkstationAssignment>();
        var issues = new List<ScheduleIssueOutput>();

        // ========== 2A：逐段贪心参考分配 ==========
        var reference = new Dictionary<long, Dictionary<TimeSpan, long>>();

        foreach (var slot in requirements.Keys.OrderBy(x => x))
        {
            var slotReqs = requirements[slot];
            var activeShifts = dayShifts
                .Where(s => IsActiveInSlot(s, slot, shiftById))
                .ToList();

            var assignedEmployeesThisSlot = new HashSet<long>();
            foreach (var workstation in slotReqs.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
            {
                var required = workstation.Value;
                if (required <= 0)
                {
                    continue;
                }

                var candidates = activeShifts
                    .Where(s => !assignedEmployeesThisSlot.Contains(s.EmployeeId))
                    .Where(s => HasSkill(s.EmployeeId, workstation.Key, skillsByEmployee))
                    .OrderByDescending(s => SkillScore(s.EmployeeId, workstation.Key, skillsByEmployee))
                    .ThenByDescending(s => s.ShiftCode)
                    .ToList();

                foreach (var candidate in candidates.Take(required))
                {
                    if (!reference.ContainsKey(candidate.EmployeeId))
                    {
                        reference[candidate.EmployeeId] = new Dictionary<TimeSpan, long>();
                    }
                    reference[candidate.EmployeeId][slot] = workstation.Key;
                    assignedEmployeesThisSlot.Add(candidate.EmployeeId);
                }
            }
        }

        // ========== 2B：确定主工作站 + 2C：初始连续分配 ==========
        foreach (var shift in dayShifts)
        {
            if (!reference.TryGetValue(shift.EmployeeId, out var refSlots) || refSlots.Count == 0)
            {
                continue;
            }

            var mainWorkstation = refSlots
                .GroupBy(x => x.Value)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => SkillScore(shift.EmployeeId, g.Key, skillsByEmployee))
                .Select(g => g.Key)
                .First();

            foreach (var slot in refSlots.Keys)
            {
                assignments.Add(new WorkstationAssignment(
                    shift.EmployeeId,
                    shift.WorkDate,
                    slot,
                    mainWorkstation,
                    SkillScore(shift.EmployeeId, mainWorkstation, skillsByEmployee),
                    shift.ShiftTemplateId));
            }
        }

        var borrowCountByEmployee = dayShifts.ToDictionary(s => s.EmployeeId, _ => 0);

        // ========== 2D + 2E：检测连续缺口块 & 块状借调 ==========
        var gapBlocks = DetectGapBlocks(assignments, requirements);

        foreach (var block in gapBlocks)
        {
            BorrowForBlock(assignments, block, dayShifts, requirements, skillsByEmployee, shiftById, borrowCountByEmployee, true);
        }

        // ========== 2F：二次连续块填补（放宽约束） ==========
        var remainingBlocks = DetectGapBlocks(assignments, requirements);

        foreach (var block in remainingBlocks)
        {
            BorrowForBlock(assignments, block, dayShifts, requirements, skillsByEmployee, shiftById, borrowCountByEmployee, false);
        }

        // ========== 2G：最终逐段兜底 ==========
        foreach (var slot in requirements.Keys.OrderBy(x => x))
        {
            foreach (var req in requirements[slot].OrderByDescending(x => x.Value))
            {
                var currentCount = assignments.Count(a =>
                    a.TimeSlot == slot && a.WorkstationId == req.Key);
                var shortfall = req.Value - currentCount;

                if (shortfall <= 0)
                {
                    continue;
                }

                var borrower = dayShifts
                    .Where(s => IsActiveInSlot(s, slot, shiftById))
                    .Where(s =>
                    {
                        var existing = assignments
                            .Where(a => a.EmployeeId == s.EmployeeId && a.TimeSlot == slot)
                            .ToList();

                        if (existing.Count == 0)
                        {
                            return false;
                        }

                        var currentWs = existing[0].WorkstationId;
                        return currentWs != req.Key && HasSkill(s.EmployeeId, req.Key, skillsByEmployee);
                    })
                    .OrderByDescending(s => SkillScore(s.EmployeeId, req.Key, skillsByEmployee))
                    .Take(shortfall)
                    .ToList();

                foreach (var b in borrower)
                {
                    Swap(assignments, b.EmployeeId, slot, req.Key, skillsByEmployee);
                }
            }
        }

        // ========== 最终缺口报告 ==========
        var finalCounts = assignments
            .GroupBy(a => (a.TimeSlot, a.WorkstationId))
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var slot in requirements.Keys)
        {
            foreach (var req in requirements[slot])
            {
                var actual = finalCounts.GetValueOrDefault((slot, req.Key));
                if (actual < req.Value)
                {
                    var shortfall = req.Value - actual;
                    issues.Add(new ScheduleIssueOutput(
                        "STAFFING_GAP",
                        shortfall >= 3 ? "ERROR" : "WARN",
                        date.WorkDate,
                        slot,
                        null,
                        req.Key,
                        $"{slot:hh\\:mm} 工作站 {req.Key} 缺 {shortfall} 人（需求 {req.Value}，实际 {actual}）"));
                }
            }
        }

        return (assignments, issues);
    }

    /// <summary>2D：检测连续缺口块，将相邻半小时段的缺口合并为块。</summary>
    private static List<GapBlock> DetectGapBlocks(
        IReadOnlyList<WorkstationAssignment> assignments,
        IReadOnlyDictionary<TimeSpan, Dictionary<long, int>> requirements)
    {
        var blocks = new List<GapBlock>();
        var counts = assignments
            .GroupBy(a => (a.TimeSlot, a.WorkstationId))
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var slot in requirements.Keys.OrderBy(x => x))
        {
            foreach (var req in requirements[slot])
            {
                var actual = counts.GetValueOrDefault((slot, req.Key));
                var shortfall = req.Value - actual;

                if (shortfall > 0)
                {
                    blocks.Add(new GapBlock(req.Key, slot, slot, shortfall, shortfall, shortfall));
                }
            }
        }

        // 按严重程度（缺口人数×块长度）降序合并相邻缺口
        var merged = new List<GapBlock>();
        foreach (var wsGroup in blocks.GroupBy(b => b.WorkstationId))
        {
            var ordered = wsGroup.OrderBy(b => b.StartSlot).ToList();
            GapBlock? current = null;

            foreach (var b in ordered)
            {
                if (current is not null &&
                    b.StartSlot == current.EndSlot.Add(TimeSpan.FromMinutes(30)) &&
                    current.WorkstationId == b.WorkstationId)
                {
                    current = current with
                    {
                        EndSlot = b.EndSlot,
                        MaxShortfall = Math.Max(current.MaxShortfall, b.MaxShortfall),
                        Severity = current.Shortfall * ((int)(current.EndSlot - current.StartSlot).TotalMinutes / 30 + 1)
                    };
                }
                else
                {
                    if (current is not null)
                    {
                        merged.Add(current);
                    }
                    current = b;
                }
            }

            if (current is not null)
            {
                merged.Add(current);
            }
        }

        return merged.OrderByDescending(b => b.Severity).ToList();
    }

    /// <summary>2E/2F：对缺口块执行块状借调。strict=true 时校验借出站全块不产生新缺口。</summary>
    private static void BorrowForBlock(
        List<WorkstationAssignment> assignments,
        GapBlock block,
        IReadOnlyList<ShiftAssignment> dayShifts,
        IReadOnlyDictionary<TimeSpan, Dictionary<long, int>> requirements,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        Dictionary<long, int> borrowCountByEmployee,
        bool strict)
    {
        var blockSlots = GetBlockSlots(block);

        var borrower = dayShifts
            .Where(s => borrowCountByEmployee.GetValueOrDefault(s.EmployeeId) < MaxBorrowPerEmployeePerDay)
            .Where(s => blockSlots.All(slot => IsActiveInSlot(s, slot, shiftById)))
            .Where(s =>
            {
                var currentWs = GetCurrentWorkstation(assignments, s.EmployeeId, blockSlots.First());
                if (currentWs == block.WorkstationId)
                {
                    return false;
                }

                if (!HasSkill(s.EmployeeId, block.WorkstationId, skillsByEmployee))
                {
                    return false;
                }

                if (strict && currentWs is not null)
                {
                    foreach (var slot in blockSlots)
                    {
                        var sourceCount = assignments.Count(a =>
                            a.TimeSlot == slot && a.WorkstationId == currentWs);
                        var sourceReq = requirements[slot].GetValueOrDefault(currentWs.Value);

                        if (sourceCount <= sourceReq)
                        {
                            return false;
                        }
                    }
                }

                return true;
            })
            .OrderByDescending(s => SkillScore(s.EmployeeId, block.WorkstationId, skillsByEmployee))
            .ThenBy(s => borrowCountByEmployee.GetValueOrDefault(s.EmployeeId))
            .ToList();

        if (borrower.Count == 0)
        {
            return;
        }

        var toBorrow = Math.Min(borrower.Count, block.Shortfall);
        for (var i = 0; i < toBorrow; i++)
        {
            var employeeId = borrower[i].EmployeeId;

            foreach (var slot in blockSlots)
            {
                Swap(assignments, employeeId, slot, block.WorkstationId, skillsByEmployee);
            }

            borrowCountByEmployee[employeeId] = borrowCountByEmployee.GetValueOrDefault(employeeId) + 1;
        }
    }

    private static List<TimeSpan> GetBlockSlots(GapBlock block)
    {
        var slots = new List<TimeSpan>();
        var current = block.StartSlot;
        while (current <= block.EndSlot)
        {
            slots.Add(current);
            current += TimeSpan.FromMinutes(30);
        }
        return slots;
    }

    private static long? GetCurrentWorkstation(
        IReadOnlyList<WorkstationAssignment> assignments,
        long employeeId,
        TimeSpan slot)
    {
        return assignments
            .Where(a => a.EmployeeId == employeeId && a.TimeSlot == slot)
            .Select(a => (long?)a.WorkstationId)
            .FirstOrDefault();
    }

    private static void Swap(
        List<WorkstationAssignment> assignments,
        long employeeId,
        TimeSpan slot,
        long targetWorkstation,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        var existing = assignments
            .Where(a => a.EmployeeId == employeeId && a.TimeSlot == slot)
            .ToList();

        if (existing.Count == 0)
        {
            return;
        }

        var updated = existing[0] with
        {
            WorkstationId = targetWorkstation,
            SkillScore = SkillScore(employeeId, targetWorkstation, skillsByEmployee)
        };

        assignments.RemoveAll(a => a.EmployeeId == employeeId && a.TimeSlot == slot);
        assignments.Add(updated);
    }

    private static bool HasSkill(long employeeId, long workstationId, IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
        => skillsByEmployee.TryGetValue(employeeId, out var skills) && skills.ContainsKey(workstationId);

    private static int SkillScore(long employeeId, long workstationId, IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
        => skillsByEmployee.TryGetValue(employeeId, out var skills) && skills.TryGetValue(workstationId, out var score) ? score : 0;

    private static bool IsActiveInSlot(
        ShiftAssignment shift,
        TimeSpan slot,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById)
    {
        if (!shiftById.TryGetValue(shift.ShiftTemplateId, out var template))
        {
            return false;
        }

        return SchedulingTimeHelper.GetShiftSlots(template.StartTime, template.EndTime, template.IsCrossDay).Contains(slot);
    }

    /// <summary>连续缺口块：同一工作站相邻时段缺口集合。严重程度=缺口人数×块长度。</summary>
    private sealed record GapBlock(
        long WorkstationId,
        TimeSpan StartSlot,
        TimeSpan EndSlot,
        int Shortfall,
        int MaxShortfall,
        int Severity);
}
