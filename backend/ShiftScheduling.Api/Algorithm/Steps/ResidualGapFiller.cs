namespace ShiftScheduling.Api.Algorithm.Steps;

/// <summary>
/// 剩余缺口兜底填充：在班次/工作站分配完成后，对仍未满足的人数需求
/// 按"全职优先、兼职填充剩余格子"的原则补班：
/// - 全职员工优先承接 ≥ 每日最低工时 的缺口块（不足最低工时的碎片块不接，
///   受"正式员工每日最低工时"约束）；
/// - 剩余缺口块（含碎片块）由兼职员工填充（兼职不受每日最低工时约束）。
/// 兜底阶段基于最终分配状态计算缺口，避免前期各阶段账面与实际的偏差导致缺口漏补。
/// </summary>
public sealed class ResidualGapFiller
{
    private const int MaxBlocksPerDay = 32;
    private const int MaxHeadcountPerBlock = 12;

    public static Dictionary<(long WorkstationId, DateOnly Date, TimeSpan Slot), int> Fill(
        SchedulingInput input,
        IReadOnlyList<RestDayAssignment> restDays,
        List<ShiftAssignment> shiftAssignments,
        List<WorkstationAssignment> workstationAssignments,
        List<ShiftTemplateInput> residualTemplates)
    {
        var restSet = restDays.ToHashSet();
        var lowSkill = input.LowSkillWorkstationIds;

        var skillsByEmployee = input.Skills
            .Where(x => x.SkillScore > 0)
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.WorkstationId, s => s.SkillScore));

        var shiftById = input.ShiftTemplates.ToDictionary(x => x.Id);
        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);

        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();

        // 剩余需求：(工作站, 日历日, 时段) → 缺人数（营业日口径：00:00-05:30 归属上一营业日）
        var residual = new Dictionary<(long WorkstationId, DateOnly Date, TimeSpan Slot), int>();
        if (dates.Count == 0)
        {
            return residual;
        }
        var firstDate = dates[0].WorkDate;

        foreach (var date in dates)
        {
            var dayType = dayTypeByDate.GetValueOrDefault(date.WorkDate) ?? "WORKDAY";
            var prevType = dayTypeByDate.GetValueOrDefault(date.WorkDate.AddDays(-1)) ?? dayType;
            foreach (var r in input.StaffingRequirements)
            {
                if (r.TimeSlot < TimeSpan.FromHours(6) && date.WorkDate == firstDate)
                {
                    continue; // 周期首日凌晨归属周期外，跳过幻影缺口
                }

                var expectedType = r.TimeSlot < TimeSpan.FromHours(6) ? prevType : dayType;
                if (r.DayType != expectedType || r.RequiredCount <= 0)
                {
                    continue;
                }

                var key = (r.WorkstationId, date.WorkDate, r.TimeSlot);
                var actual = workstationAssignments.Count(a =>
                    a.WorkstationId == r.WorkstationId &&
                    a.TimeSlot == r.TimeSlot &&
                    AssignmentCalendarDate(a, shiftById) == date.WorkDate);
                var shortfall = r.RequiredCount - actual;
                if (shortfall > 0)
                {
                    residual[key] = shortfall;
                }
            }
        }

        // 已占用时段（含跨天回绕），用于防重叠
        var busySlots = new Dictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>>();
        foreach (var a in shiftAssignments)
        {
            if (!shiftById.TryGetValue(a.ShiftTemplateId, out var t))
            {
                continue;
            }
            MarkBusy(busySlots, a.EmployeeId, t.StartTime, t.EndTime, t.IsCrossDay, a.WorkDate);
        }

        var weeklyHours = input.Employees.ToDictionary(e => e.Id, _ => 0m);
        // 单日工时（按班次起始日 workDate 累计）：单日最大工时硬约束
        var dailyHours = new Dictionary<(long EmployeeId, DateOnly WorkDate), decimal>();
        var blocksUsedPerDay = new Dictionary<DateOnly, int>();

        foreach (var date in dates)
        {
            var workDate = date.WorkDate;

            // 周工时每周一重置（WeekDay 为 MySQL DAYOFWEEK：2 = 周一）
            if (date.WeekDay == 2)
            {
                foreach (var key in weeklyHours.Keys.ToList())
                {
                    weeklyHours[key] = 0m;
                }
            }

            // 预载当日已有班次（模板班 + D 补班）的工时：候选的日/周上限校验必须计入存量，
            // 否则会出现「13:00 补班 + 19:00 店长班连轴 15h」仍通过日上限校验的叠加排班
            foreach (var a in shiftAssignments)
            {
                if (a.WorkDate != workDate || !shiftById.TryGetValue(a.ShiftTemplateId, out var t))
                {
                    continue;
                }
                var existingHours = SchedulingTimeHelper.GetShiftHours(t.StartTime, t.EndTime, t.IsCrossDay);
                dailyHours[(a.EmployeeId, workDate)] = dailyHours.GetValueOrDefault((a.EmployeeId, workDate)) + existingHours;
                weeklyHours[a.EmployeeId] = weeklyHours.GetValueOrDefault(a.EmployeeId) + existingHours;
            }

            blocksUsedPerDay[workDate] = 0;

            // 当天已有班次的员工（供每日最低工时规则使用）
            var assignedToday = busySlots
                .Where(kv => kv.Value.Any(s => s.Date == workDate))
                .Select(kv => kv.Key)
                .ToHashSet();

            foreach (var wsGroup in residual
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
                if (gapSlots.Count == 0)
                {
                    continue;
                }

                // 合并连续缺口块
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
                    if (blocksUsedPerDay[workDate] >= MaxBlocksPerDay)
                    {
                        return residual;
                    }
                    if (!BlockHasDemand(block.Start, block.EndExclusive, workstationId, workDate, residual))
                    {
                        continue;
                    }

                    blocksUsedPerDay[workDate]++;
                    var isCrossDay = block.EndExclusive <= block.Start ? 1 : 0;
                    var blockHours = SchedulingTimeHelper.GetShiftHours(block.Start, block.EndExclusive, isCrossDay);

                    var n = residualTemplates.Count + 1;
                    var template = new ShiftTemplateInput(
                        -2000 - n,
                        "R" + n,
                        "剩余缺口补班",
                        block.Start,
                        block.EndExclusive,
                        isCrossDay,
                        1,
                        new List<long> { workstationId });
                    residualTemplates.Add(template);

                    // ========== 第一轮：全职员工优先 ==========
                    // 全职只接整块 ≥ 每日最低工时的缺口（或当天已在上班可追加）。
                    // ⚠️ 权衡记录（20260825 审查，B2 已回退）：曾尝试无条件执行全职轮让
                    // 「已上班可追加」豁免对碎片块生效（管理岗缺口 27→9），但实测副手顶班日
                    // 单日工时 12h→15h（13:00 补班 + 19:00 店长班连轴转），与「16h 太长」的
                    // 业务诉求冲突，故恢复外层 if：碎片块由兼职承接，全职只接整块或已上班追加。
                    if (blockHours >= input.MinDailyWorkHours)
                    {
                        FillBlock(
                            input, workDate, workstationId, template, block,
                            residual, skillsByEmployee, lowSkill, restSet, busySlots,
                            weeklyHours, dailyHours, assignedToday, shiftAssignments, workstationAssignments,
                            residualTemplates,
                            fullTimeOnly: true, balanceFirst: false, requireSkill: false, maxHeadcount: MaxHeadcountPerBlock);
                    }

                    // ========== 第二轮：兼职填充剩余格子 ==========
                    FillBlock(
                        input, workDate, workstationId, template, block,
                        residual, skillsByEmployee, lowSkill, restSet, busySlots,
                        weeklyHours, dailyHours, assignedToday, shiftAssignments, workstationAssignments,
                        residualTemplates,
                        fullTimeOnly: false, balanceFirst: false, requireSkill: false, maxHeadcount: MaxHeadcountPerBlock);
                }
            }
        }

        // ========== 第三阶段：软需求（最好人数）均衡填充 ==========
        // 最少人数满足后，继续按最好人数补班：空闲的正式员工按「周工时最少者优先」轮转，
        // 让每人每月休息天数收敛到「每月4天、平均每周1天」，不再出现个别人休息十几天。
        var softResidual = BuildSoftResidual(input, dates, firstDate, dayTypeByDate, shiftById, workstationAssignments);
        foreach (var date in dates)
        {
            var workDate = date.WorkDate;
            blocksUsedPerDay.TryGetValue(workDate, out var usedSoFar);

            var assignedToday = busySlots
                .Where(kv => kv.Value.Any(s => s.Date == workDate))
                .Select(kv => kv.Key)
                .ToHashSet();

            foreach (var wsGroup in softResidual
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
                if (gapSlots.Count == 0)
                {
                    continue;
                }

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
                    if (usedSoFar + 1 >= MaxBlocksPerDay * 2)
                    {
                        return residual;
                    }
                    if (!BlockHasDemand(block.Start, block.EndExclusive, workstationId, workDate, softResidual))
                    {
                        continue;
                    }

                    usedSoFar++;
                    blocksUsedPerDay[workDate] = usedSoFar;
                    var isCrossDay = block.EndExclusive <= block.Start ? 1 : 0;
                    var blockHours = SchedulingTimeHelper.GetShiftHours(block.Start, block.EndExclusive, isCrossDay);

                    var n = residualTemplates.Count + 1;
                    var template = new ShiftTemplateInput(
                        -4000 - n,
                        "R" + n,
                        "软需求均衡补班",
                        block.Start,
                        block.EndExclusive,
                        isCrossDay,
                        1,
                        new List<long> { workstationId });
                    residualTemplates.Add(template);

                    // 软需求只安排正式员工，均衡轮转（兼职在最少人数阶段已按需使用）
                    FillBlock(
                        input, workDate, workstationId, template, block,
                        softResidual, skillsByEmployee, lowSkill, restSet, busySlots,
                        weeklyHours, dailyHours, assignedToday, shiftAssignments, workstationAssignments,
                        residualTemplates,
                        fullTimeOnly: true, balanceFirst: true, requireSkill: true, maxHeadcount: MaxHeadcountPerBlock);
                }
            }

            // ===== 软需求碎片日补足 =====
            // 有的正式员工当天只接到了少量软需求碎片（不足每日最低工时），
            // 再从其有技能且有软需求的时段中凑一段，把当天工时补到 ≥ 每日最低工时，
            // 避免"碎片日"被算成休息、拉高月休息天数。
            if (input.MinDailyWorkHours > 0)
            {
                foreach (var emp in input.Employees.Where(e =>
                             e.IsParttime == 0 && !restSet.Contains(new RestDayAssignment(e.Id, workDate))))
                {
                    var cur = dailyHours.GetValueOrDefault((emp.Id, workDate));
                    if (cur <= 0m || cur >= input.MinDailyWorkHours) continue;
                    if (!skillsByEmployee.TryGetValue(emp.Id, out var empSkills)) continue;

                    // 该员工当天空闲槽集合
                    var busyForDate = busySlots.TryGetValue(emp.Id, out var bs)
                        ? bs.Where(s => s.Date == workDate).Select(s => s.Slot).ToHashSet()
                        : new HashSet<TimeSpan>();
                    bool IsFree(TimeSpan slot) => !busyForDate.Contains(slot);

                    // 该日仍有软需求、且该员工有技能的时段
                    var avail = softResidual
                        .Where(kv => kv.Key.Date == workDate && kv.Value > 0 && empSkills.ContainsKey(kv.Key.WorkstationId))
                        .Select(kv => (Ws: kv.Key.WorkstationId, Slot: kv.Key.Slot))
                        .Where(x => IsFree(x.Slot))
                        .OrderBy(x => x.Slot)
                        .ToList();
                    if (avail.Count == 0) continue;

                    // 从最早可用槽开始取连续段，直到凑够每日最低工时
                    var needed = input.MinDailyWorkHours - cur;
                    var seg = new List<(long Ws, TimeSpan Slot)> { avail[0] };
                    TimeSpan prevSlot = avail[0].Slot;
                    for (var i = 1; i < avail.Count && seg.Count * 0.5m < needed; i++)
                    {
                        var expected = prevSlot.Add(TimeSpan.FromMinutes(30));
                        if (expected >= TimeSpan.FromHours(24)) expected -= TimeSpan.FromHours(24);
                        if (avail[i].Slot != expected) break; // 只取连续段
                        seg.Add(avail[i]);
                        prevSlot = avail[i].Slot;
                    }
                    if (seg.Count * 0.5m < needed) continue; // 连续段不足，放弃凑满

                    // 建补班：一个班次 + 逐槽工作站分配
                    var tplIndex = residualTemplates.Count + 1;
                    var tpl = new ShiftTemplateInput(
                        -5000 - tplIndex,
                        "R" + tplIndex,
                        "软需求碎片补足",
                        seg[0].Slot,
                        EndOf(seg[^1].Slot),
                        0,
                        1,
                        seg.Select(x => x.Ws).Distinct().ToList());
                    residualTemplates.Add(tpl);

                    var segHours = seg.Count * 0.5m;
                    shiftAssignments.Add(new ShiftAssignment(emp.Id, workDate, tpl.Id, tpl.Code, seg[0].Ws));
                    weeklyHours[emp.Id] = weeklyHours.GetValueOrDefault(emp.Id) + segHours;
                    dailyHours[(emp.Id, workDate)] = dailyHours.GetValueOrDefault((emp.Id, workDate)) + segHours;
                    foreach (var x in seg)
                    {
                        var key = (x.Ws, workDate, x.Slot);
                        if (softResidual.TryGetValue(key, out var c) && c > 0)
                        {
                            softResidual[key] = c - 1;
                        }
                        busyForDate.Add(x.Slot);
                        workstationAssignments.Add(new WorkstationAssignment(
                            emp.Id, workDate, x.Slot, x.Ws, empSkills.GetValueOrDefault(x.Ws), tpl.Id));
                    }
                    if (!busySlots.TryGetValue(emp.Id, out var set))
                    {
                        set = new HashSet<(DateOnly, TimeSpan)>();
                        busySlots[emp.Id] = set;
                    }
                    foreach (var x in seg)
                    {
                        set.Add((workDate, x.Slot));
                    }
                }
            }
        }

        return residual;
    }

    /// <summary>
    /// 全员满班：正式员工在非休息日若整天无班，直接在其最高技能岗位
    /// 补一个默认班（13:00-22:00，9 小时）。周工时上限内不受需求限制，
    /// 保证每人每月只休 default_monthly_rest_days（默认4天，每周约1天）。
    /// 独立于 Fill 的提前返回路径，由引擎在兜底填充后固定调用。
    /// </summary>
    public static void FillToFullMonth(
        SchedulingInput input,
        IReadOnlyList<RestDayAssignment> restDays,
        List<ShiftAssignment> shiftAssignments,
        List<WorkstationAssignment> workstationAssignments,
        List<ShiftTemplateInput> residualTemplates)
    {
        var restSet = restDays.ToHashSet();
        var skillsByEmployee = input.Skills
            .Where(x => x.SkillScore > 0)
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.WorkstationId, s => s.SkillScore));
        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();
        if (dates.Count == 0)
        {
            return;
        }

        var defaultStart = TimeSpan.FromHours(13);
        var defaultEnd = TimeSpan.FromHours(22);
        var defaultHours = 9m;

        // 全部模板（含前序阶段生成的），用于统计已有班次工时
        var shiftById = new Dictionary<long, ShiftTemplateInput>();
        foreach (var t in input.ShiftTemplates)
        {
            shiftById[t.Id] = t;
        }
        foreach (var t in residualTemplates)
        {
            shiftById[t.Id] = t;
        }

        var busySlots = new Dictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>>();
        foreach (var a in shiftAssignments)
        {
            if (!shiftById.TryGetValue(a.ShiftTemplateId, out var t))
            {
                continue;
            }
            MarkBusy(busySlots, a.EmployeeId, t.StartTime, t.EndTime, t.IsCrossDay, a.WorkDate);
        }

        var weeklyHours = input.Employees.ToDictionary(e => e.Id, _ => 0m);
        // 当天是否已有"起始于当天"的班次（跨天班次在次日凌晨的回绕占用不算）
        var hasShiftOnDate = new HashSet<(long EmployeeId, DateOnly WorkDate)>();
        foreach (var a in shiftAssignments)
        {
            hasShiftOnDate.Add((a.EmployeeId, a.WorkDate));
        }

        foreach (var date in dates)
        {
            var workDate = date.WorkDate;
            var weekStart = GetWeekStart(workDate);

            // 重建本周（周一~当天）已安排的工时（含前序阶段与本阶段已加的班）
            foreach (var key in weeklyHours.Keys.ToList())
            {
                weeklyHours[key] = 0m;
            }
            foreach (var a in shiftAssignments)
            {
                if (a.WorkDate < weekStart || a.WorkDate > workDate || !shiftById.TryGetValue(a.ShiftTemplateId, out var t))
                {
                    continue;
                }
                weeklyHours[a.EmployeeId] = weeklyHours.GetValueOrDefault(a.EmployeeId)
                    + SchedulingTimeHelper.GetShiftHours(t.StartTime, t.EndTime, t.IsCrossDay);
            }

            foreach (var emp in input.Employees.Where(e =>
                         e.IsParttime == 0 && !restSet.Contains(new RestDayAssignment(e.Id, workDate))))
            {
                // 当天已有起始班次则跳过（跨天班次次日凌晨的回绕不算）
                if (hasShiftOnDate.Contains((emp.Id, workDate)))
                {
                    continue;
                }
                // 周工时上限校验
                if (weeklyHours.GetValueOrDefault(emp.Id) + defaultHours > emp.MaxWeeklyHours)
                {
                    continue;
                }
                if (!skillsByEmployee.TryGetValue(emp.Id, out var sk) || sk.Count == 0)
                {
                    continue;
                }

                // 最高技能岗位
                var ws = sk.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                var score = sk[ws];

                var tplIndex = residualTemplates.Count + 1;
                var tpl = new ShiftTemplateInput(
                    -6000 - tplIndex,
                    "R" + tplIndex,
                    "满班安排",
                    defaultStart,
                    defaultEnd,
                    0,
                    1,
                    new List<long> { ws });
                residualTemplates.Add(tpl);
                shiftById[tpl.Id] = tpl;

                shiftAssignments.Add(new ShiftAssignment(emp.Id, workDate, tpl.Id, tpl.Code, ws));
                hasShiftOnDate.Add((emp.Id, workDate));
                MarkBusy(busySlots, emp.Id, defaultStart, defaultEnd, 0, workDate);
                weeklyHours[emp.Id] = weeklyHours.GetValueOrDefault(emp.Id) + defaultHours;

                foreach (var slot in SchedulingTimeHelper.GetShiftSlots(defaultStart, defaultEnd, 0))
                {
                    workstationAssignments.Add(new WorkstationAssignment(emp.Id, workDate, slot, ws, score, tpl.Id));
                }
            }
        }
    }

    /// <summary>自然周周一（与工时超限检查口径一致）。</summary>
    private static DateOnly GetWeekStart(DateOnly date)
    {
        var dayOffset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-dayOffset);
    }

    /// <summary>
    /// 软需求剩余量（按最好人数口径）：(工作站, 日历日, 时段) → 还能再补的人数。
    /// input 中 IdealCount 已解析为 ≥ RequiredCount（BuildInputAsync 口径）。
    /// </summary>
    private static Dictionary<(long WorkstationId, DateOnly Date, TimeSpan Slot), int> BuildSoftResidual(
        SchedulingInput input,
        IReadOnlyList<DateParameterInput> dates,
        DateOnly firstDate,
        IReadOnlyDictionary<DateOnly, string> dayTypeByDate,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyList<WorkstationAssignment> workstationAssignments)
    {
        var soft = new Dictionary<(long WorkstationId, DateOnly Date, TimeSpan Slot), int>();
        foreach (var date in dates)
        {
            var dayType = dayTypeByDate.GetValueOrDefault(date.WorkDate) ?? "WORKDAY";
            var prevType = dayTypeByDate.GetValueOrDefault(date.WorkDate.AddDays(-1)) ?? dayType;
            foreach (var r in input.StaffingRequirements)
            {
                if (r.TimeSlot < TimeSpan.FromHours(6) && date.WorkDate == firstDate)
                {
                    continue;
                }

                var expectedType = r.TimeSlot < TimeSpan.FromHours(6) ? prevType : dayType;
                if (r.DayType != expectedType || r.IdealCount <= 0)
                {
                    continue;
                }

                var key = (r.WorkstationId, date.WorkDate, r.TimeSlot);
                var actual = workstationAssignments.Count(a =>
                    a.WorkstationId == r.WorkstationId &&
                    a.TimeSlot == r.TimeSlot &&
                    AssignmentCalendarDate(a, shiftById) == date.WorkDate);
                var shortfall = r.IdealCount - actual;
                if (shortfall > 0)
                {
                    soft[key] = shortfall;
                }
            }
        }

        return soft;
    }

    /// <summary>
    /// 基于兜底填充后的最终剩余缺口生成 STAFFING_GAP 问题
    /// （此前各阶段报告的口径已与最终分配状态不一致，缺口以本方法为准）。
    /// </summary>
    public static List<ScheduleIssueOutput> BuildGapIssues(
        SchedulingInput input,
        IReadOnlyDictionary<(long WorkstationId, DateOnly Date, TimeSpan Slot), int> residual)
    {
        var issues = new List<ScheduleIssueOutput>();
        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);
        var firstDate = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .Select(x => x.WorkDate)
            .OrderBy(x => x)
            .FirstOrDefault();

        // 提醒类工作站（如工程维修岗）：整周期一条汇总提醒
        var warnOnly = new Dictionary<long, (int TotalSlots, int MaxShortfall, DateOnly FirstDate, DateOnly LastDate)>();

        foreach (var kv in residual)
        {
            var (wsId, date, slot) = kv.Key;
            var shortfall = kv.Value;
            if (shortfall <= 0)
            {
                continue;
            }

            if (input.WarnOnlyGapWorkstations.ContainsKey(wsId))
            {
                if (warnOnly.TryGetValue(wsId, out var acc))
                {
                    warnOnly[wsId] = (
                        acc.TotalSlots + 1,
                        Math.Max(acc.MaxShortfall, shortfall),
                        acc.FirstDate < date ? acc.FirstDate : date,
                        acc.LastDate > date ? acc.LastDate : date);
                }
                else
                {
                    warnOnly[wsId] = (1, shortfall, date, date);
                }
                continue;
            }

            // 需求/实际：从人数需求配置反查（营业日口径与填充阶段一致）
            var prevType = dayTypeByDate.GetValueOrDefault(date.AddDays(-1)) ?? dayTypeByDate.GetValueOrDefault(date);
            var expectedType = slot < TimeSpan.FromHours(6) ? prevType : dayTypeByDate.GetValueOrDefault(date) ?? "WORKDAY";
            var required = input.StaffingRequirements
                .Where(r => r.WorkstationId == wsId && r.DayType == expectedType && r.TimeSlot == slot)
                .Select(r => r.RequiredCount)
                .FirstOrDefault();
            if (required <= 0 && slot < TimeSpan.FromHours(6) && date == firstDate)
            {
                continue; // 周期首日凌晨的幻影缺口不报告
            }

            var actual = Math.Max(0, required - shortfall);
            var isLowSkill = input.LowSkillWorkstationIds.ContainsKey(wsId);
            var description = $"{slot:hh\\:mm} 工作站 {wsId} 缺 {shortfall} 人（需求 {required}，实际 {actual}）";
            if (isLowSkill)
            {
                description += "。该岗位技术含量低，建议寻找兼职人员临时填补";
            }
            var severity = shortfall >= 3 && !isLowSkill ? "ERROR" : "WARN";
            issues.Add(new ScheduleIssueOutput("STAFFING_GAP", severity, date, slot, null, wsId, description));
        }

        foreach (var (wsId, acc) in warnOnly)
        {
            issues.Add(new ScheduleIssueOutput(
                "STAFFING_GAP",
                "WARN",
                acc.FirstDate,
                null,
                null,
                wsId,
                $"工作站「{input.WarnOnlyGapWorkstations[wsId]}」整周期共 {acc.TotalSlots} 个时段存在岗位缺口（{acc.FirstDate:yyyy-MM-dd} 至 {acc.LastDate:yyyy-MM-dd}，单时段峰值缺 {acc.MaxShortfall} 人），请人工安排或调整人数需求"));
        }

        return issues;
    }

    private static void FillBlock(
        SchedulingInput input,
        DateOnly workDate,
        long workstationId,
        ShiftTemplateInput template,
        (TimeSpan Start, TimeSpan EndExclusive) block,
        Dictionary<(long WorkstationId, DateOnly Date, TimeSpan Slot), int> residual,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, bool> lowSkill,
        HashSet<RestDayAssignment> restSet,
        Dictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>> busySlots,
        Dictionary<long, decimal> weeklyHours,
        Dictionary<(long EmployeeId, DateOnly WorkDate), decimal> dailyHours,
        HashSet<long> assignedToday,
        List<ShiftAssignment> shiftAssignments,
        List<WorkstationAssignment> workstationAssignments,
        List<ShiftTemplateInput> residualTemplates,
        bool fullTimeOnly,
        bool balanceFirst,
        bool requireSkill,
        int maxHeadcount)
    {
        var blockHours = SchedulingTimeHelper.GetShiftHours(template.StartTime, template.EndTime, template.IsCrossDay);
        var usedThisBlock = 0;
        // 偏好学习（feature/schedule-pref-learning）：按当天 day_type 取偏好分
        var workDateDayType = input.DateParameters
            .Where(d => d.WorkDate == workDate)
            .Select(d => d.DayType)
            .FirstOrDefault() ?? "WORKDAY";

        while (BlockHasDemand(block.Start, block.EndExclusive, workstationId, workDate, residual) && usedThisBlock < maxHeadcount)
        {
            var candidateList = input.Employees
                .Where(e => fullTimeOnly ? e.IsParttime == 0 : e.IsParttime == 1)
                .Where(e => !restSet.Contains(new RestDayAssignment(e.Id, workDate)))
                // 审查修复（B）：不再因「块与员工已有班次部分重叠」整块拒绝——
                // 只要求块内存在至少一个可接槽位，重叠部分在分配时裁剪。
                .Where(e => HasAnyAvailableSlot(busySlots, e.Id, template, workDate))
                .Where(e => weeklyHours.GetValueOrDefault(e.Id) + blockHours <= e.MaxWeeklyHours)
                // 单日最大工时硬约束（0 = 不限制；按岗位配置，缺省回退全局默认）
                .Where(e => input.GetMaxDailyWorkHours(e.Department) <= 0m
                            || dailyHours.GetValueOrDefault((e.Id, workDate)) + blockHours <= input.GetMaxDailyWorkHours(e.Department))
                .Where(e => requireSkill
                    ? (skillsByEmployee.TryGetValue(e.Id, out var sk) && sk.ContainsKey(workstationId))
                    : HasStationSkill(e.Id, workstationId, skillsByEmployee, lowSkill))
                .Where(e => e.IsParttime == 1
                            || assignedToday.Contains(e.Id)
                            || blockHours >= input.MinDailyWorkHours)
                .ToList();

            IEnumerable<EmployeeInput> orderedCandidates;
            if (balanceFirst)
            {
                // 软需求均衡填充：周工时最少者优先，让空闲员工轮转上班（休息天数收敛到每月约4天）
                orderedCandidates = candidateList
                    .OrderBy(e => weeklyHours.GetValueOrDefault(e.Id))
                    .ThenByDescending(e => PreferenceScoring.EffectiveScore(
                        StationSkillScore(e.Id, workstationId, skillsByEmployee),
                        PreferenceScoring.ForWorkstation(e.Id, workstationId, workDateDayType, input),
                        input.PreferenceWeight,
                        PreferenceScoring.SingleStationSkillMax))
                    .ThenBy(e => e.Id);
            }
            else
            {
                orderedCandidates = candidateList
                    .OrderByDescending(e => PreferenceScoring.EffectiveScore(
                        StationSkillScore(e.Id, workstationId, skillsByEmployee),
                        PreferenceScoring.ForWorkstation(e.Id, workstationId, workDateDayType, input),
                        input.PreferenceWeight,
                        PreferenceScoring.SingleStationSkillMax))  // 0-1 连续权重：技能分×(1−w) + 偏好归一化分×w
                    .ThenBy(e => weeklyHours.GetValueOrDefault(e.Id))
                    .ThenBy(e => e.Id);
            }

            EmployeeInput? candidate = null;
            (TimeSpan Start, TimeSpan EndExclusive)? chosenSegment = null;
            foreach (var e in orderedCandidates)
            {
                // 计算该候选在块内的最长可接连续段（跳过与已有班次重叠的槽），
                // 只补不重叠部分；剩余缺口由后续循环继续尝试其他人/段。
                var seg = LongestAvailableSegment(busySlots, e.Id, template, workDate);
                if (seg is null)
                {
                    continue;
                }
                var segH = SchedulingTimeHelper.GetShiftHours(seg.Value.Start, seg.Value.EndExclusive, seg.Value.EndExclusive <= seg.Value.Start ? 1 : 0);
                // 正式员工当天累计工时（含本段）不得低于每日最低工时：
                // 防止「已有 0.5h 碎片 + 再补 1h」这类低于 6.5h 的碎班工作日
                if (e.IsParttime == 0
                    && dailyHours.GetValueOrDefault((e.Id, workDate)) + segH < input.MinDailyWorkHours)
                {
                    continue;
                }
                candidate = e;
                chosenSegment = seg;
                break;
            }

            if (candidate is null || chosenSegment is null)
            {
                break;
            }
            var segment = chosenSegment.Value;

            var segIsCrossDay = segment.EndExclusive <= segment.Start ? 1 : 0;
            var segHours = SchedulingTimeHelper.GetShiftHours(segment.Start, segment.EndExclusive, segIsCrossDay);

            var segIndex = residualTemplates.Count + 1;
            var segTemplate = new ShiftTemplateInput(
                -3000 - segIndex,
                "R" + segIndex,
                "剩余缺口补班",
                segment.Start,
                segment.EndExclusive,
                segIsCrossDay,
                1,
                template.WorkstationIds);
            residualTemplates.Add(segTemplate);

            // 补充班次分配 + 工作站时段分配（仅可接子段）
            var slotList = SchedulingTimeHelper.GetShiftSlots(segTemplate.StartTime, segTemplate.EndTime, segTemplate.IsCrossDay);
            shiftAssignments.Add(new ShiftAssignment(candidate.Id, workDate, segTemplate.Id, segTemplate.Code, workstationId));
            MarkBusy(busySlots, candidate.Id, segTemplate.StartTime, segTemplate.EndTime, segTemplate.IsCrossDay, workDate);
            assignedToday.Add(candidate.Id);
            weeklyHours[candidate.Id] = weeklyHours.GetValueOrDefault(candidate.Id) + segHours;
            dailyHours[(candidate.Id, workDate)] = dailyHours.GetValueOrDefault((candidate.Id, workDate)) + segHours;

            var score = StationSkillScore(candidate.Id, workstationId, skillsByEmployee);
            foreach (var slot in slotList)
            {
                var calendarDate = SchedulingTimeHelper.SlotCalendarDate(slot, segTemplate.StartTime, workDate);
                var key = (workstationId, calendarDate, slot);
                if (residual.TryGetValue(key, out var cnt) && cnt > 0)
                {
                    residual[key] = cnt - 1;
                }
                workstationAssignments.Add(new WorkstationAssignment(
                    candidate.Id, workDate, slot, workstationId, score, segTemplate.Id));
            }

            usedThisBlock++;
        }
    }

    private static bool BlockHasDemand(
        TimeSpan start,
        TimeSpan endExclusive,
        long workstationId,
        DateOnly workDate,
        IReadOnlyDictionary<(long WorkstationId, DateOnly Date, TimeSpan Slot), int> residual)
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(start, endExclusive, endExclusive <= start ? 1 : 0);
        return slots.Any(slot =>
            residual.TryGetValue((workstationId, workDate, slot), out var cnt) && cnt > 0);
    }

    private static bool HasStationSkill(
        long employeeId,
        long workstationId,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee,
        IReadOnlyDictionary<long, bool> lowSkill)
        => (skillsByEmployee.TryGetValue(employeeId, out var skills) && skills.ContainsKey(workstationId))
           || lowSkill.ContainsKey(workstationId);

    private static int StationSkillScore(
        long employeeId,
        long workstationId,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
        => skillsByEmployee.TryGetValue(employeeId, out var skills)
           && skills.TryGetValue(workstationId, out var score)
            ? score
            : 0;

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

    private static void MarkBusy(
        Dictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>> busySlots,
        long employeeId,
        TimeSpan start,
        TimeSpan end,
        int isCrossDay,
        DateOnly workDate)
    {
        if (!busySlots.TryGetValue(employeeId, out var set))
        {
            set = new HashSet<(DateOnly, TimeSpan)>();
            busySlots[employeeId] = set;
        }

        foreach (var slot in SchedulingTimeHelper.GetShiftSlots(start, end, isCrossDay))
        {
            set.Add((SchedulingTimeHelper.SlotCalendarDate(slot, start, workDate), slot));
        }
    }

    /// <summary>
    /// 块内是否存在至少一个与员工当天班次不重叠的槽位。
    /// 审查修复（B）：替代整块 HasOverlap 判断——块与员工班次部分重叠时，
    /// 允许补不重叠的部分（重叠部分由 LongestAvailableSegment 裁剪）。
    /// </summary>
    private static bool HasAnyAvailableSlot(
        IReadOnlyDictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>> busySlots,
        long employeeId,
        ShiftTemplateInput shift,
        DateOnly workDate)
    {
        if (!busySlots.TryGetValue(employeeId, out var set))
        {
            return true;
        }

        return SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay)
            .Any(slot => !set.Contains((SchedulingTimeHelper.SlotCalendarDate(slot, shift.StartTime, workDate), slot)));
    }

    /// <summary>
    /// 计算员工在块内的最长可接连续段（跳过与已有班次重叠的槽）。
    /// 无可用槽返回 null。
    /// </summary>
    private static (TimeSpan Start, TimeSpan EndExclusive)? LongestAvailableSegment(
        IReadOnlyDictionary<long, HashSet<(DateOnly Date, TimeSpan Slot)>> busySlots,
        long employeeId,
        ShiftTemplateInput shift,
        DateOnly workDate)
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);
        busySlots.TryGetValue(employeeId, out var busySet);

        var bestStart = TimeSpan.Zero;
        var bestEnd = TimeSpan.Zero;
        var bestLen = 0;
        var curStart = TimeSpan.Zero;
        var curLen = 0;
        TimeSpan? prev = null;

        foreach (var slot in slots)
        {
            var calendarDate = SchedulingTimeHelper.SlotCalendarDate(slot, shift.StartTime, workDate);
            var available = busySet is null || !busySet.Contains((calendarDate, slot));
            if (!available)
            {
                prev = null;
                continue;
            }

            if (prev is null || !IsConsecutiveSlot(prev.Value, slot))
            {
                curStart = slot;
                curLen = 0;
            }

            curLen++;
            prev = slot;
            if (curLen > bestLen)
            {
                bestLen = curLen;
                bestStart = curStart;
                bestEnd = EndOf(slot);
            }
        }

        return bestLen > 0 ? (bestStart, bestEnd) : null;
    }

    /// <summary>两个槽位是否连续（30 分钟步进，处理跨午夜回绕）。</summary>
    private static bool IsConsecutiveSlot(TimeSpan prev, TimeSpan next)
    {
        var expected = prev.Add(TimeSpan.FromMinutes(30));
        if (expected >= TimeSpan.FromHours(24))
        {
            expected -= TimeSpan.FromHours(24);
        }

        return next == expected;
    }

    private static TimeSpan EndOf(TimeSpan lastSlot)
    {
        var end = lastSlot.Add(TimeSpan.FromMinutes(30));
        return end >= TimeSpan.FromHours(24) ? end - TimeSpan.FromHours(24) : end;
    }
}
