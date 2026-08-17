namespace ShiftScheduling.Api.Algorithm;

/// <summary>
/// 阶段四：班中休息分配（每次固定 30 分钟）。
/// 规则：
/// - 班次时长 &lt; 4 小时不休息；
/// - 上班 2 小时内、下班 1 小时内不能休息；
/// - 休息与高峰禁休时段完全不重叠（默认 20:00-22:00，门店可配置多条）；
/// - 优先错峰：岗位冗余足够时休息，不影响在岗人数；
/// - 冗余不足时借调（当班 + 有该站技能 + 自己岗位有冗余），不熟练借调产生 INFO 提示；
/// - 无法覆盖时输出 BREAK_UNCOVERED 警告，由店长人工处理。
/// 休息照算工时（不扣减 WorkHours）。
/// </summary>
public sealed class BreakAllocator
{
    private const int BreakMinutes = 30;
    private const int MinShiftMinutes = 240;      // < 4h 不休息
    private const int MinAfterStartMinutes = 120; // 上班 2 小时内禁休
    private const int MinBeforeEndMinutes = 60;   // 下班 1 小时内禁休
    private const int MaxBorrowPerEmployeePerDay = 2;

    public IReadOnlyList<BreakAssignment> Allocate(
        SchedulingInput input,
        IReadOnlyList<ShiftAssignment> shiftAssignments,
        IReadOnlyList<WorkstationAssignment> workstationAssignments,
        List<ScheduleIssueOutput>? issueCollector = null)
    {
        var breaks = new List<BreakAssignment>();

        // 高峰禁休时段：无配置时用默认 20:00-22:00
        var peaks = input.PeakRestrictedHours.Count > 0
            ? input.PeakRestrictedHours.ToList()
            : new List<PeakRestrictedHourInput> { new(TimeSpan.FromHours(20), TimeSpan.FromHours(22)) };

        var shiftById = input.ShiftTemplates.ToDictionary(x => x.Id);
        var employeeName = input.Employees.ToDictionary(x => x.Id, x => x.Name);

        var skillsByEmployee = input.Skills
            .Where(x => x.SkillScore > 0)
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(s => s.WorkstationId, s => (Score: s.SkillScore, IsPrimary: s.IsPrimarySkill)));

        // 员工在某个时段的原工作站：(employeeId, date) -> slot -> workstationId
        var wsByEmployeeDateSlot = workstationAssignments
            .GroupBy(a => (a.EmployeeId, a.WorkDate))
            .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.TimeSlot, a => a.WorkstationId));

        // 员工当天主工作站（分配次数最多的站）
        var mainWsByEmployeeDate = workstationAssignments
            .GroupBy(a => (a.EmployeeId, a.WorkDate))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(a => a.WorkstationId)
                      .OrderByDescending(x => x.Count())
                      .Select(x => x.Key)
                      .First());

        // 需求：(dayType) -> (slot, workstationId) -> requiredCount
        var requirementsByDayType = input.StaffingRequirements
            .GroupBy(r => r.DayType)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => (r.TimeSlot, r.WorkstationId))
                      .ToDictionary(x => x.Key, x => x.Max(r => r.RequiredCount)));

        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);

        // 基础在岗人数
        var baseCounts = workstationAssignments
            .GroupBy(a => (a.WorkDate, a.TimeSlot, a.WorkstationId))
            .ToDictionary(g => g.Key, g => g.Count());

        // 动态增量：休息者离岗 -1；借调者顶岗 +1、离开原岗位 -1
        var delta = new Dictionary<(DateOnly Date, TimeSpan Slot, long Ws), int>();

        int EffectiveCount(DateOnly date, TimeSpan slot, long ws)
            => baseCounts.GetValueOrDefault((date, slot, ws)) + delta.GetValueOrDefault((date, slot, ws));

        bool OverlapsPeak(int absStartMin)
        {
            var end = absStartMin + BreakMinutes;
            return peaks.Any(p => absStartMin < (int)p.EndTime.TotalMinutes && end > (int)p.StartTime.TotalMinutes);
        }

        // 与最近高峰边界的距离（分钟），用于同等冗余下"离高峰越远越好"
        int PeakDistance(int absStartMin)
        {
            var end = absStartMin + BreakMinutes;
            var dist = int.MaxValue;
            foreach (var p in peaks)
            {
                var ps = (int)p.StartTime.TotalMinutes;
                var pe = (int)p.EndTime.TotalMinutes;
                if (end <= ps)
                {
                    dist = Math.Min(dist, ps - end);
                }
                else if (absStartMin >= pe)
                {
                    dist = Math.Min(dist, absStartMin - pe);
                }
                else
                {
                    dist = Math.Min(dist, 0);
                }
            }

            return dist;
        }

        // 当天已安排的休息槽计数（用于分散休息）；借调次数（每人每天上限）
        var breakCountBySlot = new Dictionary<int, int>();
        var borrowCount = new Dictionary<long, int>();

        var shiftsByDate = shiftAssignments
            .GroupBy(x => x.WorkDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var date in input.DateParameters
                     .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
                     .OrderBy(x => x.WorkDate))
        {
            if (!shiftsByDate.TryGetValue(date.WorkDate, out var dayShifts))
            {
                continue;
            }

            var dayType = dayTypeByDate.GetValueOrDefault(date.WorkDate) ?? "WORKDAY";
            var dayReqs = requirementsByDayType.TryGetValue(dayType, out var reqs)
                ? reqs
                : new Dictionary<(TimeSpan, long), int>();

            // 每日重置休息分散计数与借调次数
            breakCountBySlot.Clear();
            borrowCount.Clear();

            foreach (var shift in dayShifts.OrderBy(s => shiftById[s.ShiftTemplateId].StartTime.TotalMinutes))
            {
                if (!shiftById.TryGetValue(shift.ShiftTemplateId, out var template))
                {
                    continue;
                }

                var durationMin = (int)(SchedulingTimeHelper.GetShiftHours(template.StartTime, template.EndTime, template.IsCrossDay) * 60);
                if (durationMin < MinShiftMinutes)
                {
                    continue; // 短班不休息
                }

                var employeeId = shift.EmployeeId;
                var startMin = (int)template.StartTime.TotalMinutes;
                var windowEndOffset = durationMin - MinBeforeEndMinutes - BreakMinutes;

                // 候选休息槽（绝对分钟，30 分钟对齐，不与高峰重叠）
                var candidates = new List<int>();
                for (var off = MinAfterStartMinutes; off <= windowEndOffset; off += BreakMinutes)
                {
                    var abs = (startMin + off) % 1440;
                    if (!OverlapsPeak(abs))
                    {
                        candidates.Add(abs);
                    }
                }

                var hasWs = mainWsByEmployeeDate.TryGetValue((employeeId, date.WorkDate), out var mainWs) &&
                            wsByEmployeeDateSlot.ContainsKey((employeeId, date.WorkDate));

                if (candidates.Count == 0)
                {
                    // 高峰把整个可休窗口挤没了：休息安排在窗口起点，告警交人工
                    var forced = (startMin + MinAfterStartMinutes) % 1440;
                    breaks.Add(new BreakAssignment(employeeId, date.WorkDate, TimeSpan.FromMinutes(forced), null, false, hasWs ? mainWs : null));
                    issueCollector?.Add(new ScheduleIssueOutput(
                        "BREAK_UNCOVERED",
                        "WARN",
                        date.WorkDate,
                        TimeSpan.FromMinutes(forced),
                        employeeId,
                        hasWs ? mainWs : null,
                        $"员工 {EmpName(employeeId)} 无合规休息窗口（高峰禁休与上班2小时/下班1小时规则冲突），休息被迫安排在 {Fmt(forced)}，请人工调整"));
                    continue;
                }

                // 选择休息槽：错峰优先。
                // 1) 先保证休息后不缺人（Redundancy >= 0）；
                // 2) 在安全的槽位里挑“已安排休息人数最少”的槽位 → 大家错开休息，
                //    而不是都挤在冗余最大的同一时段（例如 00:00）；
                // 3) 同槽人数相同时再比冗余大小、离高峰距离。
                var best = candidates
                    .Select(abs => new
                    {
                        Abs = abs,
                        Slot = TimeSpan.FromMinutes(abs),
                        Redundancy = hasWs
                            ? EffectiveCount(date.WorkDate, TimeSpan.FromMinutes(abs), mainWs) - 1
                              - dayReqs.GetValueOrDefault((TimeSpan.FromMinutes(abs), mainWs))
                            : int.MaxValue,
                        PeakDist = PeakDistance(abs),
                        SlotBreaks = breakCountBySlot.GetValueOrDefault(abs)
                    })
                    .OrderByDescending(x => x.Redundancy >= 0)
                    .ThenBy(x => x.SlotBreaks)
                    .ThenByDescending(x => x.Redundancy)
                    .ThenByDescending(x => x.PeakDist)
                    .First();

                if (best.Redundancy >= 0)
                {
                    ApplyBreak(employeeId, date.WorkDate, best.Abs, null, false, hasWs ? mainWs : 0, hasWs);
                }
                else
                {
                    // 尝试借调：按缺人最少排序的候选槽逐个找借调人
                    long? borrower = null;
                    var inexperienced = false;
                    var chosenAbs = best.Abs;

                    foreach (var cand in candidates
                                 .Select(abs => new
                                 {
                                     Abs = abs,
                                     Slot = TimeSpan.FromMinutes(abs),
                                     Shortfall = hasWs
                                         ? dayReqs.GetValueOrDefault((TimeSpan.FromMinutes(abs), mainWs))
                                           - (EffectiveCount(date.WorkDate, TimeSpan.FromMinutes(abs), mainWs) - 1)
                                         : 0,
                                     SlotBreaks = breakCountBySlot.GetValueOrDefault(abs)
                                 })
                                 .Where(x => x.Shortfall > 0)
                                 .OrderBy(x => x.Shortfall)
                                 // 缺人相同时优先选休息人数少的槽位（同样错开）
                                 .ThenBy(x => x.SlotBreaks)
                                 .ThenBy(x => x.Abs))
                    {
                        var found = FindBorrower(
                            date.WorkDate,
                            cand.Slot,
                            mainWs,
                            employeeId,
                            dayShifts,
                            shiftById,
                            wsByEmployeeDateSlot,
                            skillsByEmployee,
                            dayReqs,
                            borrowCount,
                            EffectiveCount,
                            out var bId,
                            out var inexp);

                        if (found)
                        {
                            borrower = bId;
                            inexperienced = inexp;
                            chosenAbs = cand.Abs;
                            break;
                        }
                    }

                    if (borrower is not null)
                    {
                        ApplyBreak(employeeId, date.WorkDate, chosenAbs, borrower, inexperienced, hasWs ? mainWs : 0, hasWs);

                        if (inexperienced)
                        {
                            issueCollector?.Add(new ScheduleIssueOutput(
                                "BREAK_BORROW_INEXPERIENCED",
                                "INFO",
                                date.WorkDate,
                                TimeSpan.FromMinutes(chosenAbs),
                                employeeId,
                                hasWs ? mainWs : null,
                                $"员工 {EmpName(employeeId)} 在 {Fmt(chosenAbs)} 休息，由不熟练员工 {EmpName(borrower.Value)} 顶岗，请注意"));
                        }
                    }
                    else
                    {
                        ApplyBreak(employeeId, date.WorkDate, best.Abs, null, false, hasWs ? mainWs : 0, hasWs);
                        issueCollector?.Add(new ScheduleIssueOutput(
                            "BREAK_UNCOVERED",
                            "WARN",
                            date.WorkDate,
                            TimeSpan.FromMinutes(best.Abs),
                            employeeId,
                            hasWs ? mainWs : null,
                            $"员工 {EmpName(employeeId)} 在 {Fmt(best.Abs)} 休息无人顶岗，该岗位缺人，请人工处理"));
                    }
                }
            }
        }

        return breaks;

        // ===== 局部函数 =====

        string EmpName(long id)
            => employeeName.TryGetValue(id, out var n) ? n : $"员工{id}";

        void ApplyBreak(long employeeId, DateOnly date, int absStart, long? coverId, bool inexp, long? breakWs, bool hasWs)
        {
            breaks.Add(new BreakAssignment(employeeId, date, TimeSpan.FromMinutes(absStart), coverId, inexp, breakWs));
            breakCountBySlot[absStart] = breakCountBySlot.GetValueOrDefault(absStart) + 1;

            if (!hasWs || breakWs <= 0)
            {
                return;
            }

            var slot = TimeSpan.FromMinutes(absStart);
            var ws = breakWs ?? 0;
            var key = (date, slot, ws);
            delta[key] = delta.GetValueOrDefault(key) - 1; // 休息者离岗

            if (coverId is not null && ws > 0 &&
                wsByEmployeeDateSlot.TryGetValue((coverId.Value, date), out var ownBySlot) &&
                ownBySlot.TryGetValue(slot, out var ownWs))
            {
                delta[key] += 1; // 借调者顶岗
                var ownKey = (date, slot, ownWs);
                delta[ownKey] = delta.GetValueOrDefault(ownKey) - 1; // 借调者离开原岗位
            }
        }
    }

    /// <summary>
    /// 为休息员工的某个时段寻找借调人：
    /// 当班覆盖该时段 + 有目标站技能 + 该时段原分配在其他站 + 离开原岗位后原站不缺人。
    /// 主技能（IsPrimarySkill=1）优先，技能分高者优先；每人每天最多借调 2 次。
    /// </summary>
    private static bool FindBorrower(
        DateOnly date,
        TimeSpan slot,
        long mainWs,
        long restingEmployeeId,
        IReadOnlyList<ShiftAssignment> dayShifts,
        IReadOnlyDictionary<long, ShiftTemplateInput> shiftById,
        IReadOnlyDictionary<(long EmployeeId, DateOnly Date), Dictionary<TimeSpan, long>> wsByEmployeeDateSlot,
        IReadOnlyDictionary<long, Dictionary<long, (int Score, int IsPrimary)>> skillsByEmployee,
        IReadOnlyDictionary<(TimeSpan Slot, long Ws), int> dayReqs,
        Dictionary<long, int> borrowCount,
        Func<DateOnly, TimeSpan, long, int> effectiveCount,
        out long borrowerId,
        out bool inexperienced)
    {
        borrowerId = 0;
        inexperienced = false;

        var bestScore = -1;
        var bestPrimary = false;

        foreach (var s in dayShifts)
        {
            var eid = s.EmployeeId;
            if (eid == restingEmployeeId)
            {
                continue;
            }

            if (borrowCount.GetValueOrDefault(eid) >= MaxBorrowPerEmployeePerDay)
            {
                continue;
            }

            if (!shiftById.TryGetValue(s.ShiftTemplateId, out var template))
            {
                continue;
            }

            // 当班且覆盖该时段
            if (!SchedulingTimeHelper.GetShiftSlots(template.StartTime, template.EndTime, template.IsCrossDay).Contains(slot))
            {
                continue;
            }

            // 有目标站技能
            if (!skillsByEmployee.TryGetValue(eid, out var skills) ||
                !skills.TryGetValue(mainWs, out var skill))
            {
                continue;
            }

            // 该时段原分配在其他站
            if (!wsByEmployeeDateSlot.TryGetValue((eid, date), out var ownBySlot) ||
                !ownBySlot.TryGetValue(slot, out var ownWs) ||
                ownWs == mainWs)
            {
                continue;
            }

            // 离开原岗位后原站不缺人
            var ownInDuty = effectiveCount(date, slot, ownWs);
            var ownDemand = dayReqs.GetValueOrDefault((slot, ownWs));
            if (ownInDuty - 1 < ownDemand)
            {
                continue;
            }

            // 主技能优先，技能分高者优先
            var isPrimary = skill.IsPrimary == 1;
            if (isPrimary && !bestPrimary)
            {
                bestScore = skill.Score;
                bestPrimary = true;
                borrowerId = eid;
            }
            else if (isPrimary == bestPrimary && skill.Score > bestScore)
            {
                bestScore = skill.Score;
                borrowerId = eid;
            }
        }

        if (borrowerId == 0)
        {
            return false;
        }

        borrowCount[borrowerId] = borrowCount.GetValueOrDefault(borrowerId) + 1;
        inexperienced = !bestPrimary;
        return true;
    }

    private static string Fmt(int minutes)
        => $"{(minutes / 60) % 24:D2}:{minutes % 60:D2}";
}
