namespace ShiftScheduling.Api.Algorithm.Steps;

/// <summary>
/// 阶段一：休息日分配。
/// 规则：
/// - 每员工休息天数按周期比例折算（默认每月 4 天，约每周 1 天）；
/// - 行政员工（管理/行政/工程）：可在周五、周六休息，且优先安排周六/周五/周日；
/// - 服务员工（楼面/厨房/吧台等）：周五、周六为营业高峰不允许休息，优先平日休；
/// - 按"每天配额"均匀铺开，避免扎堆。
/// </summary>
public sealed class RestDayAllocator
{
    public IReadOnlyList<RestDayAssignment> Allocate(SchedulingInput input)
    {
        var assignments = new List<RestDayAssignment>();
        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();

        if (dates.Count == 0)
        {
            return assignments;
        }

        // 每人休息天数按周期比例折算（每月默认 4 天）
        var restDaysTarget = Math.Max(0, (int)Math.Round(input.DefaultMonthlyRestDays * dates.Count / 30.0));
        if (restDaysTarget <= 0)
        {
            return assignments;
        }

        // 全部日期作为候选（含周五周六，行政可休）
        var allDays = dates.OrderBy(d => d.WorkDate).ToList();

        // 总休息日 = 员工数 × 每人休息天数
        var totalRestDays = input.Employees.Count * restDaysTarget;

        // 行政员工数（决定高峰日配额上限）
        var adminCount = input.Employees.Count(e => CanRestOnPeakDay(e.Department));

        // 每天配额：平均分配，余数给靠前的天；周五/周六为高峰日，配额不超过行政人数
        var baseQuota = totalRestDays / allDays.Count;
        var remainder = totalRestDays % allDays.Count;
        var quotaByDate = new Dictionary<DateOnly, int>();
        for (var i = 0; i < allDays.Count; i++)
        {
            var q = baseQuota + (i < remainder ? 1 : 0);
            var isPeakDay = IsPeakDay(allDays[i]);
            if (isPeakDay)
            {
                q = Math.Min(q, adminCount);
            }
            quotaByDate[allDays[i].WorkDate] = q;
        }

        // P1-4 修复：改用计数器，每个员工可分配多天休息（上限 restDaysTarget 天）
        var restDayCounts = new Dictionary<long, int>();
        // 每天每部门已休息的部门集合
        var restedDeptsByDate = new Dictionary<DateOnly, HashSet<string>>();

        // 按日期顺序，为每天都尽量凑满配额
        foreach (var day in allDays)
        {
            var quota = quotaByDate[day.WorkDate];
            if (quota <= 0)
            {
                continue;
            }

            var isPeakDay = IsPeakDay(day);

            if (!restedDeptsByDate.ContainsKey(day.WorkDate))
                restedDeptsByDate[day.WorkDate] = new HashSet<string>();

            var restrictedDepts = restedDeptsByDate[day.WorkDate];

            // 候选：未达到休息天数上限；高峰日仅行政员工可休；当天每部门不超1人
            var orderedEmployees = input.Employees
                .Where(e => restDayCounts.GetValueOrDefault(e.Id, 0) < restDaysTarget)
                .Where(e => !isPeakDay || CanRestOnPeakDay(e.Department))
                .OrderBy(e => restDayCounts.GetValueOrDefault(e.Id, 0))
                // 偏好学习（feature/schedule-pref-learning）：店长习惯让谁在该类型日休息则优先
                .ThenByDescending(e => PreferenceScoring.ForRest(e.Id, day.DayType, input))
                .ThenBy(e => PreferenceRank(e.Department, day))
                .ThenBy(e => e.Id);

            var candidates = new List<EmployeeInput>();
            foreach (var e in orderedEmployees)
            {
                if (candidates.Count >= quota) break;
                if (restrictedDepts.Contains(e.Department)) continue;
                candidates.Add(e);
                restrictedDepts.Add(e.Department);
            }

            foreach (var employee in candidates)
            {
                assignments.Add(new RestDayAssignment(employee.Id, day.WorkDate));
                restDayCounts[employee.Id] = restDayCounts.GetValueOrDefault(employee.Id, 0) + 1;
            }
        }

        // 兜底：配额有缺口时，为剩余员工补一个"当日休息人数最少且允许"的日期
        var restCountByDate = assignments
            .GroupBy(x => x.WorkDate)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var employee in input.Employees.Where(e => restDayCounts.GetValueOrDefault(e.Id, 0) < restDaysTarget))
        {
            var isAdministrative = IsAdministrativeDepartment(employee.Department);

            var best = allDays
                .Where(d => CanRestOnPeakDay(employee.Department) || !IsPeakDay(d))
                .OrderBy(d => restCountByDate.GetValueOrDefault(d.WorkDate))
                .ThenByDescending(d => PreferenceScoring.ForRest(employee.Id, d.DayType, input))
                .ThenBy(d => PreferenceRank(employee.Department, d))
                .ThenBy(d => d.WorkDate.DayNumber)
                .FirstOrDefault();

            if (best is not null)
            {
                assignments.Add(new RestDayAssignment(employee.Id, best.WorkDate));
                restDayCounts[employee.Id] = restDayCounts.GetValueOrDefault(employee.Id, 0) + 1;
                restCountByDate[best.WorkDate] = restCountByDate.GetValueOrDefault(best.WorkDate) + 1;
            }
        }

        // ============================================================
        // 修复：强制保证"连续工作天数 ≤ MaxConsecutiveWorkDays"
        // 原逻辑里 max_consecutive_work_days 只用于事后预警，不约束休息分配，
        // 导致连续工作总比上限多一天。这里在休息分配后主动打断超限连续段。
        // ============================================================
        var maxConsecutive = input.MaxConsecutiveWorkDays;
        if (maxConsecutive > 0)
        {
            // 每个员工已分配的休息日集合
            var restSetByEmp = assignments
                .GroupBy(x => x.EmployeeId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.WorkDate).ToHashSet());

            foreach (var employee in input.Employees)
            {
                if (!restSetByEmp.TryGetValue(employee.Id, out var restSet))
                {
                    restSet = new HashSet<DateOnly>();
                    restSetByEmp[employee.Id] = restSet;
                }

                var streak = 0;
                DateOnly? streakStart = null;

                for (var i = 0; i < allDays.Count; i++)
                {
                    var day = allDays[i];
                    if (restSet.Contains(day.WorkDate))
                    {
                        streak = 0;
                        streakStart = null;
                        continue;
                    }

                    if (streakStart is null)
                        streakStart = day.WorkDate;
                    streak++;

                    // 连续工作达到上限：下一天必须休息，否则连续天数将超过上限
                    if (streak >= maxConsecutive && i + 1 < allDays.Count)
                    {
                        var nextDay = allDays[i + 1];
                        // 若下一天是高峰日且员工高峰日不能休，则该段无法强制打断（现实约束）
                        if (CanRestOnPeakDay(employee.Department) || !IsPeakDay(nextDay))
                        {
                            if (!restSet.Contains(nextDay.WorkDate))
                            {
                                assignments.Add(new RestDayAssignment(employee.Id, nextDay.WorkDate));
                                restSet.Add(nextDay.WorkDate);
                                restDayCounts[employee.Id] = restDayCounts.GetValueOrDefault(employee.Id) + 1;
                                restCountByDate[nextDay.WorkDate] = restCountByDate.GetValueOrDefault(nextDay.WorkDate) + 1;
                                // 跳到下下天继续
                                streak = 0;
                                streakStart = null;
                                i++; // for 循环会再 +1
                                continue;
                            }
                        }
                    }
                }
            }
        }

        return assignments;
    }

    /// <summary>周五(MySQL DAYOFWEEK=6) / 周六(7) 视为营业高峰日。</summary>
    private static bool IsPeakDay(DateParameterInput day)
        => day.WeekDay == 6 || day.WeekDay == 7;

    /// <summary>部门 + 星期偏好排序：越小越优先。
    /// 行政员工：周六 > 周五 > 周日 > 平日（行政周末休、周五周六可休）；
    /// 服务员工：平日(周一~周四) > 周日 > 周五/周六（高峰日几乎不排）。</summary>
    private static int PreferenceRank(string department, DateParameterInput day)
    {
        var canRestOnPeakDay = CanRestOnPeakDay(department);
        if (canRestOnPeakDay)
        {
            if (day.WeekDay == 7) return 0; // 周六
            if (day.WeekDay == 6) return 1; // 周五
            if (day.WeekDay == 1) return 2; // 周日
            return 3;                        // 平日
        }

        if (day.WeekDay == 6 || day.WeekDay == 7) return 9; // 服务员工高峰日尽量不休
        if (day.WeekDay == 1) return 1;                     // 周日
        return 0;                                            // 平日（周一~周四）
    }

    /// <summary>是否可在周五/周六高峰日休息：仅管理、行政、工程（保洁周末走不开，归入不可休）。</summary>
    internal static bool CanRestOnPeakDay(string department)
        => department switch
        {
            "管理" or "行政" or "工程" => true,
            _ => false
        };

    internal static bool IsAdministrativeDepartment(string department)
        => department switch
        {
            "管理" or "行政" or "工程" or "保洁" => true,
            _ => false
        };
}