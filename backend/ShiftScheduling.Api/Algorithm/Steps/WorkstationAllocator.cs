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

        // P3-14 修复：缓存每个班次的时段列表，避免 IsActiveInSlot 重复重建
        var shiftSlotsCache = input.ShiftTemplates.ToDictionary(
            s => s.Id,
            s => SchedulingTimeHelper.GetShiftSlots(s.StartTime, s.EndTime, s.IsCrossDay));

        var shiftsByDate = shiftAssignments
            .GroupBy(x => x.WorkDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();

        // 营业日口径：某日历日 00:00-05:30 归属上一营业日。
        // 需求字典【全局持久】且键带日历日期：(日历日, 时段) -> (工作站 -> 最少人数)。
        // 跨天班次的午夜回绕部分按次日日历键计数，修复"凌晨需求匹配错营业日"。
        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);
        var requirements = BuildRequirements(input, dates, dayTypeByDate);
        var idealBySlot = BuildRequirements(input, dates, dayTypeByDate, ideal: true);

        var lastDate = dates.Count > 0 ? dates[^1].WorkDate : input.EndDate;

        foreach (var date in dates)
        {
            // 即使当天没有新开始的班次，也要做最终缺口报告：
            // 当天凌晨（<06:00）的需求由前一天的跨天班次覆盖，无当日班次时仍须如实上报缺口
            var dayShifts = shiftsByDate.GetValueOrDefault(date.WorkDate) ?? new List<ShiftAssignment>();

            var isLastDate = date.WorkDate == lastDate;
            // assignments 为全周期共享列表：次日统计缺口/借调时需要看到前一天跨天班次的午夜回绕行
            var dayIssues = AllocateForDate(
                input, date.WorkDate, dayShifts, skillsByEmployee, shiftById, shiftSlotsCache,
                requirements, idealBySlot, isLastDate, assignments);
            issueCollector?.AddRange(dayIssues);
        }

        return assignments;
    }

    /// <summary>迭代日 d 的排班窗口：当日全部时段 + 次日凌晨（<06:00，跨天班次可覆盖）。</summary>
    private static bool InWindow(DateOnly keyDate, TimeSpan slot, DateOnly date)
        => keyDate == date || (keyDate == date.AddDays(1) && slot < TimeSpan.FromHours(6));

    private static Dictionary<(DateOnly Date, TimeSpan Slot), Dictionary<long, int>> BuildRequirements(
        SchedulingInput input,
        IReadOnlyList<DateParameterInput> dates,
        IReadOnlyDictionary<DateOnly, string> dayTypeByDate,
        bool ideal = false)
    {
        var result = new Dictionary<(DateOnly Date, TimeSpan Slot), Dictionary<long, int>>();
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

                var key = (date.WorkDate, r.TimeSlot);
                if (!result.TryGetValue(key, out var wsDict))
                {
                    wsDict = new Dictionary<long, int>();
                    result[key] = wsDict;
                }

                wsDict[r.WorkstationId] = Math.Max(wsDict.GetValueOrDefault(r.WorkstationId), value);
            }
        }

        return result;
    }

    private static IReadOnlyList<ScheduleIssueOutput> AllocateForDate(
        SchedulingInput input,
        DateOnly dayDate,
        IReadOnlyList<ShiftAssignment> dayShifts,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, IReadOnlyList<TimeSpan>> shiftSlotsCache,
        IReadOnlyDictionary<(DateOnly Date, TimeSpan Slot), Dictionary<long, int>> requirements,
        IReadOnlyDictionary<(DateOnly Date, TimeSpan Slot), Dictionary<long, int>> idealBySlot,
        bool isLastDate,
        List<WorkstationAssignment> assignments)
    {
        var issues = new List<ScheduleIssueOutput>();

        // 需求键带日历日期；分配记录 WorkDate 仍按班次开始日期存储（持久化约定），
        // 比较时通过 AssignmentCalendarDate 换算实际日历日期。
        // ========== 2A：逐段贪心参考分配 ==========
        var reference = new Dictionary<long, Dictionary<(DateOnly Date, TimeSpan Slot), long>>();

        foreach (var (slotDate, slot) in requirements.Keys
                     .Where(k => InWindow(k.Date, k.Slot, dayDate))
                     .OrderBy(k => k.Date)
                     .ThenBy(k => k.Slot))
        {
            var slotReqs = requirements[(slotDate, slot)];
            var activeShifts = dayShifts
                .Where(s => IsActiveInSlot(s, slotDate, slot, shiftById, shiftSlotsCache))
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
                        reference[candidate.EmployeeId] = new Dictionary<(DateOnly, TimeSpan), long>();
                    }
                    reference[candidate.EmployeeId][(slotDate, slot)] = workstation.Key;
                    assignedEmployeesThisSlot.Add(candidate.EmployeeId);
                }
            }
        }

        // ========== 2A+：软性需求（最好人数）补充参考分配 ==========
        foreach (var (slotDate, slot) in idealBySlot.Keys
                     .Where(k => InWindow(k.Date, k.Slot, dayDate))
                     .OrderBy(k => k.Date)
                     .ThenBy(k => k.Slot))
        {
            var slotIdeals = idealBySlot[(slotDate, slot)];
            var activeShifts = dayShifts
                .Where(s => IsActiveInSlot(s, slotDate, slot, shiftById, shiftSlotsCache))
                .ToList();

            var assignedEmployeesThisSlot = activeShifts
                .Where(s => reference.TryGetValue(s.EmployeeId, out var refs) && refs.ContainsKey((slotDate, slot)))
                .Select(s => s.EmployeeId)
                .ToHashSet();

            foreach (var workstation in slotIdeals.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
            {
                var ideal = workstation.Value;
                if (ideal <= 0)
                {
                    continue;
                }

                var current = activeShifts.Count(s =>
                    reference.TryGetValue(s.EmployeeId, out var refs) &&
                    refs.TryGetValue((slotDate, slot), out var ws) && ws == workstation.Key);
                var extra = ideal - current;
                if (extra <= 0)
                {
                    continue;
                }

                var candidates = activeShifts
                    .Where(s => !assignedEmployeesThisSlot.Contains(s.EmployeeId))
                    .Where(s => HasSkill(s.EmployeeId, workstation.Key, skillsByEmployee))
                    .OrderByDescending(s => SkillScore(s.EmployeeId, workstation.Key, skillsByEmployee))
                    .ThenByDescending(s => s.ShiftCode)
                    .ToList();

                foreach (var candidate in candidates.Take(extra))
                {
                    if (!reference.ContainsKey(candidate.EmployeeId))
                    {
                        reference[candidate.EmployeeId] = new Dictionary<(DateOnly, TimeSpan), long>();
                    }

                    reference[candidate.EmployeeId][(slotDate, slot)] = workstation.Key;
                    assignedEmployeesThisSlot.Add(candidate.EmployeeId);
                }
            }
        }

        // ========== 2B：确定主工作站 + 2C：初始连续分配 ==========
        foreach (var shift in dayShifts)
        {
            if (!shiftById.TryGetValue(shift.ShiftTemplateId, out var template))
            {
                continue;
            }

            long? mainWorkstation;
            if (shift.WorkstationId is not null && HasSkill(shift.EmployeeId, shift.WorkstationId.Value, skillsByEmployee))
            {
                // 优先沿用班次分配阶段选定的工作站（ShiftAllocator 按需求扣减的口径），
                // 保证两个分配器口径一致，避免"班次算外吧、这里改保洁"的错位。
                mainWorkstation = shift.WorkstationId.Value;
            }
            else if (reference.TryGetValue(shift.EmployeeId, out var refSlots) && refSlots.Count > 0)
            {
                mainWorkstation = refSlots
                    .GroupBy(x => x.Value)
                    .OrderByDescending(g => g.Count())
                    .ThenByDescending(g => SkillScore(shift.EmployeeId, g.Key, skillsByEmployee))
                    .Select(g => g.Key)
                    .First();
            }
            else
            {
                // 修复：未进入参考分配的当班员工（无任何所需岗位技能/仅在富余时段当班）
                // 也要有工作站——兜底分配到该班次可覆盖的最高技能岗位；
                // 完全无技能则上报问题，而不是静默消失。
                mainWorkstation = FallbackWorkstation(shift, shiftById, skillsByEmployee);
                if (mainWorkstation is null)
                {
                    issues.Add(new ScheduleIssueOutput(
                        "UNASSIGNED_STAFF",
                        "WARN",
                        shift.WorkDate,
                        template.StartTime,
                        shift.EmployeeId,
                        null,
                        "员工在班但无任何工作站技能，未分配岗位，请人工处理"));
                    continue;
                }
            }

            // 修复：为班次覆盖的【全部】时段分配主工作站（含午夜回绕部分），
            // 而不是只分配参考分配中出现过的时段。
            var shiftSlots = shiftSlotsCache.TryGetValue(template.Id, out var cached)
                ? cached
                : SchedulingTimeHelper.GetShiftSlots(template.StartTime, template.EndTime, template.IsCrossDay);
            foreach (var slot in shiftSlots)
            {
                // 单时段人数上限：主站在该时段(按实际日历日)已满员(>=最好人数)时不再追加，
                // 避免整班固定主站造成单时段人数超过需求配置（该员工此时段视为浮动，不显示岗位）
                var slotDate = SchedulingTimeHelper.SlotCalendarDate(slot, template.StartTime, shift.WorkDate);
                var atCeiling = idealBySlot.TryGetValue((slotDate, slot), out var slotIdeals) &&
                                slotIdeals.TryGetValue(mainWorkstation.Value, out var idealLimit) &&
                                assignments.Count(a =>
                                    a.WorkstationId == mainWorkstation.Value &&
                                    a.TimeSlot == slot &&
                                    AssignmentCalendarDate(a, shiftById) == slotDate) >= idealLimit;

                if (atCeiling)
                {
                    continue;
                }

                assignments.Add(new WorkstationAssignment(
                    shift.EmployeeId,
                    shift.WorkDate,
                    slot,
                    mainWorkstation.Value,
                    SkillScore(shift.EmployeeId, mainWorkstation.Value, skillsByEmployee),
                    shift.ShiftTemplateId));
            }
        }

        var borrowCountByEmployee = dayShifts.ToDictionary(s => s.EmployeeId, _ => 0);

        // ========== 2D + 2E：检测连续缺口块 & 块状借调 ==========
        var gapBlocks = DetectGapBlocks(assignments, requirements, dayDate, shiftById, shiftSlotsCache);

        foreach (var block in gapBlocks)
        {
            BorrowForBlock(assignments, block, dayShifts, requirements, skillsByEmployee, shiftById, shiftSlotsCache, borrowCountByEmployee, true);
        }

        // ========== 2F：二次连续块填补（放宽约束） ==========
        var remainingBlocks = DetectGapBlocks(assignments, requirements, dayDate, shiftById, shiftSlotsCache);

        foreach (var block in remainingBlocks)
        {
            BorrowForBlock(assignments, block, dayShifts, requirements, skillsByEmployee, shiftById, shiftSlotsCache, borrowCountByEmployee, false);
        }

        // ========== 2G：最终逐段兜底 ==========
        foreach (var (slotDate, slot) in requirements.Keys
                     .Where(k => InWindow(k.Date, k.Slot, dayDate))
                     .OrderBy(k => k.Date)
                     .ThenBy(k => k.Slot))
        {
            foreach (var req in requirements[(slotDate, slot)].OrderByDescending(x => x.Value))
            {
                var currentCount = assignments.Count(a =>
                    a.TimeSlot == slot &&
                    a.WorkstationId == req.Key &&
                    AssignmentCalendarDate(a, shiftById) == slotDate);
                var shortfall = req.Value - currentCount;

                if (shortfall <= 0)
                {
                    continue;
                }

                var borrower = dayShifts
                    .Where(s => IsActiveInSlot(s, slotDate, slot, shiftById, shiftSlotsCache))
                    .Where(s =>
                    {
                        var existing = assignments
                            .Where(a => a.EmployeeId == s.EmployeeId &&
                                        a.TimeSlot == slot &&
                                        AssignmentCalendarDate(a, shiftById) == slotDate)
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
                    Swap(assignments, b.EmployeeId, slotDate, slot, req.Key, skillsByEmployee, shiftById, shiftSlotsCache);
                }
            }
        }

        // ========== 最终缺口报告 ==========
        // 仅报告【处理日当天日历日】的缺口：次日凌晨的缺口在下一轮迭代仍可被
        // 当天 00:00 起班的 D 班次等填补，下一轮再报告；周期最后一天则连带报告
        // 其次日凌晨（无后续迭代）。
        var reportKeys = requirements.Keys
            .Where(k => k.Date == dayDate || (isLastDate && k.Date == dayDate.AddDays(1)))
            .OrderBy(k => k.Date)
            .ThenBy(k => k.Slot)
            .ToList();

        foreach (var (slotDate, slot) in reportKeys)
        {
            foreach (var req in requirements[(slotDate, slot)])
            {
                var actual = assignments.Count(a =>
                    a.TimeSlot == slot &&
                    a.WorkstationId == req.Key &&
                    AssignmentCalendarDate(a, shiftById) == slotDate);
                if (actual >= req.Value)
                {
                    continue;
                }

                var shortfall = req.Value - actual;
                var isLowSkill = input.LowSkillWorkstationIds.ContainsKey(req.Key);
                var description = $"{slot:hh\\:mm} 工作站 {req.Key} 缺 {shortfall} 人（需求 {req.Value}，实际 {actual}）";
                if (isLowSkill)
                {
                    description += "。该岗位技术含量低，建议寻找兼职人员临时填补";
                }
                // 低技能岗位（可兼职替补）缺口不升级为 ERROR，避免结构性人手不足阻塞发布
                var severity = shortfall >= 3 && !isLowSkill ? "ERROR" : "WARN";
                issues.Add(new ScheduleIssueOutput(
                    "STAFFING_GAP",
                    severity,
                    slotDate,
                    slot,
                    null,
                    req.Key,
                    description));
            }
        }

        return issues;
    }

    /// <summary>无参考分配的当班员工：兜底选择该班次可覆盖的最高技能岗位；无技能返回 null。</summary>
    private static long? FallbackWorkstation(
        ShiftAssignment shift,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        if (!shiftById.TryGetValue(shift.ShiftTemplateId, out var template))
        {
            return null;
        }

        if (!skillsByEmployee.TryGetValue(shift.EmployeeId, out var skills) || skills.Count == 0)
        {
            return null;
        }

        return template.WorkstationIds
            .Where(skills.ContainsKey)
            .OrderByDescending(ws => skills[ws])
            .ThenBy(ws => ws)
            .FirstOrDefault();
    }

    /// <summary>分配记录的实际日历日期（跨天班次午夜回绕部分属于次日）。</summary>
    private static DateOnly AssignmentCalendarDate(
        WorkstationAssignment assignment,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById)
    {
        if (shiftById.TryGetValue(assignment.ShiftTemplateId, out var template))
        {
            return SchedulingTimeHelper.SlotCalendarDate(assignment.TimeSlot, template.StartTime, assignment.WorkDate);
        }

        return assignment.WorkDate;
    }

    /// <summary>2D：检测连续缺口块（可跨午夜合并），将相邻半小时段的缺口合并为块。</summary>
    private static List<GapBlock> DetectGapBlocks(
        IReadOnlyList<WorkstationAssignment> assignments,
        IReadOnlyDictionary<(DateOnly Date, TimeSpan Slot), Dictionary<long, int>> requirements,
        DateOnly dayDate,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, IReadOnlyList<TimeSpan>> shiftSlotsCache)
    {
        var blocks = new List<GapBlock>();
        var counts = new Dictionary<(DateOnly Date, TimeSpan Slot, long Ws), int>();
        foreach (var a in assignments)
        {
            var key = (AssignmentCalendarDate(a, shiftById), a.TimeSlot, a.WorkstationId);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        foreach (var (slotDate, slot) in requirements.Keys
                     .Where(k => InWindow(k.Date, k.Slot, dayDate))
                     .OrderBy(k => k.Date)
                     .ThenBy(k => k.Slot))
        {
            foreach (var req in requirements[(slotDate, slot)])
            {
                var actual = counts.GetValueOrDefault((slotDate, slot, req.Key));
                var shortfall = req.Value - actual;

                if (shortfall > 0)
                {
                    blocks.Add(new GapBlock(req.Key, slotDate, slot, slot, shortfall, shortfall));
                }
            }
        }

        // 按严重程度（缺口人数×块长度）降序合并相邻缺口（含跨午夜：23:30 → 次日 00:00）
        var merged = new List<GapBlock>();
        foreach (var wsGroup in blocks.GroupBy(b => b.WorkstationId))
        {
            var ordered = wsGroup.OrderBy(b => b.Date).ThenBy(b => b.StartSlot).ToList();
            GapBlock? current = null;

            foreach (var b in ordered)
            {
                if (current is not null && current.WorkstationId == b.WorkstationId && IsAdjacent(current, b))
                {
                    current = current with
                    {
                        EndSlot = b.EndSlot,
                        // 更新 Shortfall 为区间最大缺口（借调人数按峰值）
                        Shortfall = Math.Max(current.Shortfall, b.Shortfall),
                        MaxShortfall = Math.Max(current.MaxShortfall, b.MaxShortfall)
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

    /// <summary>相邻判定：同一天内 30 分钟相连，或 23:30 与次日 00:00 跨午夜相连。</summary>
    private static bool IsAdjacent(GapBlock current, GapBlock next)
    {
        if (current.Date == next.Date)
        {
            return next.StartSlot == current.EndSlot.Add(TimeSpan.FromMinutes(30));
        }

        return next.Date == current.Date.AddDays(1)
            && current.EndSlot == TimeSpan.FromMinutes(23 * 60 + 30)
            && next.StartSlot == TimeSpan.Zero;
    }

    /// <summary>2E/2F：对缺口块执行块状借调。strict=true 时校验借出站全块不产生新缺口。</summary>
    private static void BorrowForBlock(
        List<WorkstationAssignment> assignments,
        GapBlock block,
        IReadOnlyList<ShiftAssignment> dayShifts,
        IReadOnlyDictionary<(DateOnly Date, TimeSpan Slot), Dictionary<long, int>> requirements,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, IReadOnlyList<TimeSpan>> shiftSlotsCache,
        Dictionary<long, int> borrowCountByEmployee,
        bool strict)
    {
        var blockSlots = GetBlockSlots(block);

        var borrower = dayShifts
            .Where(s => borrowCountByEmployee.GetValueOrDefault(s.EmployeeId) < MaxBorrowPerEmployeePerDay)
            .Where(s => blockSlots.All(p => IsActiveInSlot(s, p.Date, p.Slot, shiftById, shiftSlotsCache)))
            .Where(s =>
            {
                var currentWs = GetCurrentWorkstation(assignments, s.EmployeeId, blockSlots[0].Date, blockSlots[0].Slot, shiftById, shiftSlotsCache);
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
                    foreach (var p in blockSlots)
                    {
                        var sourceCount = assignments.Count(a =>
                            a.TimeSlot == p.Slot &&
                            a.WorkstationId == currentWs &&
                            AssignmentCalendarDate(a, shiftById) == p.Date);
                        var sourceReq = requirements.GetValueOrDefault((p.Date, p.Slot))?.GetValueOrDefault(currentWs.Value) ?? 0;

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

            foreach (var p in blockSlots)
            {
                Swap(assignments, employeeId, p.Date, p.Slot, block.WorkstationId, skillsByEmployee, shiftById, shiftSlotsCache);
            }

            borrowCountByEmployee[employeeId] = borrowCountByEmployee.GetValueOrDefault(employeeId) + 1;
        }
    }

    private static List<(DateOnly Date, TimeSpan Slot)> GetBlockSlots(GapBlock block)
    {
        var slots = new List<(DateOnly, TimeSpan)>();
        var date = block.Date;
        var current = block.StartSlot;
        while (true)
        {
            slots.Add((date, current));
            if (current == block.EndSlot)
            {
                break;
            }

            current += TimeSpan.FromMinutes(30);
            if (current >= TimeSpan.FromHours(24))
            {
                current -= TimeSpan.FromHours(24);
                date = date.AddDays(1);
            }
        }
        return slots;
    }

    private static long? GetCurrentWorkstation(
        IReadOnlyList<WorkstationAssignment> assignments,
        long employeeId,
        DateOnly date,
        TimeSpan slot,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, IReadOnlyList<TimeSpan>> shiftSlotsCache)
    {
        _ = shiftSlotsCache;
        return assignments
            .Where(a => a.EmployeeId == employeeId &&
                        a.TimeSlot == slot &&
                        AssignmentCalendarDate(a, shiftById) == date)
            .Select(a => (long?)a.WorkstationId)
            .FirstOrDefault();
    }

    private static void Swap(
        List<WorkstationAssignment> assignments,
        long employeeId,
        DateOnly date,
        TimeSpan slot,
        long targetWorkstation,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, IReadOnlyList<TimeSpan>> shiftSlotsCache)
    {
        var existing = assignments
            .Where(a => a.EmployeeId == employeeId &&
                        a.TimeSlot == slot &&
                        AssignmentCalendarDate(a, shiftById) == date)
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

        assignments.RemoveAll(a => a.EmployeeId == employeeId &&
                                   a.TimeSlot == slot &&
                                   AssignmentCalendarDate(a, shiftById) == date);
        assignments.Add(updated);
    }

    private static bool HasSkill(long employeeId, long workstationId, IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
        => skillsByEmployee.TryGetValue(employeeId, out var skills) && skills.ContainsKey(workstationId);

    private static int SkillScore(long employeeId, long workstationId, IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
        => skillsByEmployee.TryGetValue(employeeId, out var skills) && skills.TryGetValue(workstationId, out var score) ? score : 0;

    private static bool IsActiveInSlot(
        ShiftAssignment shift,
        DateOnly slotDate,
        TimeSpan slot,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<long, IReadOnlyList<TimeSpan>> shiftSlotsCache)
    {
        if (!shiftById.TryGetValue(shift.ShiftTemplateId, out var template))
        {
            return false;
        }

        var slots = shiftSlotsCache.TryGetValue(template.Id, out var cachedSlots)
            ? cachedSlots
            : SchedulingTimeHelper.GetShiftSlots(template.StartTime, template.EndTime, template.IsCrossDay);

        // 时段必须属于该班次，且实际日历日期与需求键一致（跨天班次午夜回绕部分属于次日）
        return slots.Contains(slot) &&
               SchedulingTimeHelper.SlotCalendarDate(slot, template.StartTime, shift.WorkDate) == slotDate;
    }

    /// <summary>连续缺口块：同一工作站相邻时段缺口集合。严重程度=区间最大缺口×块长度。</summary>
    private sealed record GapBlock(
        long WorkstationId,
        DateOnly Date,
        TimeSpan StartSlot,
        TimeSpan EndSlot,
        int Shortfall,
        int MaxShortfall)
    {
        /// <summary>块内半小时段数量（支持跨午夜回绕）。</summary>
        public int SlotCount
        {
            get
            {
                var count = 0;
                var current = StartSlot;
                while (true)
                {
                    count++;
                    if (current == EndSlot)
                    {
                        break;
                    }

                    current += TimeSpan.FromMinutes(30);
                    if (current >= TimeSpan.FromHours(24))
                    {
                        current -= TimeSpan.FromHours(24);
                    }
                }
                return count;
            }
        }

        /// <summary>严重程度 = 区间最大缺口人数 × 块长度（计算属性，随合并自动更新）。</summary>
        public int Severity => MaxShortfall * SlotCount;
    }
}
