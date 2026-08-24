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
                            weeklyHours, assignedToday, shiftAssignments, workstationAssignments,
                            residualTemplates,
                            fullTimeOnly: true, maxHeadcount: MaxHeadcountPerBlock);
                    }

                    // ========== 第二轮：兼职填充剩余格子 ==========
                    FillBlock(
                        input, workDate, workstationId, template, block,
                        residual, skillsByEmployee, lowSkill, restSet, busySlots,
                        weeklyHours, assignedToday, shiftAssignments, workstationAssignments,
                        residualTemplates,
                        fullTimeOnly: false, maxHeadcount: MaxHeadcountPerBlock);
                }
            }
        }

        return residual;
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
        HashSet<long> assignedToday,
        List<ShiftAssignment> shiftAssignments,
        List<WorkstationAssignment> workstationAssignments,
        List<ShiftTemplateInput> residualTemplates,
        bool fullTimeOnly,
        int maxHeadcount)
    {
        var blockHours = SchedulingTimeHelper.GetShiftHours(template.StartTime, template.EndTime, template.IsCrossDay);
        var usedThisBlock = 0;

        while (BlockHasDemand(block.Start, block.EndExclusive, workstationId, workDate, residual) && usedThisBlock < maxHeadcount)
        {
            var candidates = input.Employees
                .Where(e => fullTimeOnly ? e.IsParttime == 0 : e.IsParttime == 1)
                .Where(e => !restSet.Contains(new RestDayAssignment(e.Id, workDate)))
                // 审查修复（B）：不再因「块与员工已有班次部分重叠」整块拒绝——
                // 只要求块内存在至少一个可接槽位，重叠部分在分配时裁剪。
                .Where(e => HasAnyAvailableSlot(busySlots, e.Id, template, workDate))
                .Where(e => weeklyHours.GetValueOrDefault(e.Id) + blockHours <= e.MaxWeeklyHours)
                .Where(e => HasStationSkill(e.Id, workstationId, skillsByEmployee, lowSkill))
                .Where(e => e.IsParttime == 1
                            || assignedToday.Contains(e.Id)
                            || blockHours >= input.MinDailyWorkHours)
                .OrderByDescending(e => StationSkillScore(e.Id, workstationId, skillsByEmployee))
                .ThenBy(e => weeklyHours.GetValueOrDefault(e.Id))
                .ThenBy(e => e.Id)
                .ToList();

            var candidate = candidates.FirstOrDefault();
            if (candidate is null)
            {
                break;
            }

            // 计算该候选在块内的最长可接连续段（跳过与已有班次重叠的槽），
            // 只补不重叠部分；剩余缺口由后续循环继续尝试其他人/段。
            var segment = LongestAvailableSegment(busySlots, candidate.Id, template, workDate);
            if (segment is null)
            {
                break; // 理论上不可达（HasAnyAvailableSlot 已保证至少一个可接槽），防御性退出
            }

            var segIsCrossDay = segment.Value.EndExclusive <= segment.Value.Start ? 1 : 0;
            var segHours = SchedulingTimeHelper.GetShiftHours(segment.Value.Start, segment.Value.EndExclusive, segIsCrossDay);

            var segIndex = residualTemplates.Count + 1;
            var segTemplate = new ShiftTemplateInput(
                -3000 - segIndex,
                "R" + segIndex,
                "剩余缺口补班",
                segment.Value.Start,
                segment.Value.EndExclusive,
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
