using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Algorithm.Steps;
using ShiftScheduling.Api.Infrastructure.Persistence;

namespace ShiftScheduling.Api.Algorithm;

public sealed class SchedulingEngine
{
    private readonly ShiftSchedulingDbContext _dbContext;

    public SchedulingEngine(ShiftSchedulingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SchedulingOutput> GenerateAsync(
        long storeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var input = await BuildInputAsync(storeId, startDate, endDate, cancellationToken);
        return Execute(input);
    }

    public SchedulingOutput Execute(SchedulingInput input)
    {
        var restDayAllocator = new RestDayAllocator();
        var shiftAllocator = new ShiftAllocator();
        var workstationAllocator = new WorkstationAllocator();

        // 阶段一：休息日分配
        var restDays = restDayAllocator.Allocate(input);

        // 已批准请假强制视为休息日，确保请假员工在请假期间不被排班
        var effectiveRestDays = MergeApprovedLeaves(restDays, input);

        // 阶段二：班次分配（模板班次 + 按需求缺口自动生成的临时班次 D1/D2…）
        var generatedTemplates = new List<ShiftTemplateInput>();
        var shiftAssignments = shiftAllocator.Allocate(input, effectiveRestDays, generatedTemplates);

        // 临时班次并入模板列表，供工作站分配/休息分配/汇总解析班次时间
        var effectiveInput = generatedTemplates.Count == 0
            ? input
            : input with { ShiftTemplates = input.ShiftTemplates.Concat(generatedTemplates).ToList() };

        // 阶段三：工作站分配（收集岗位缺口）
        var staffingGaps = new List<ScheduleIssueOutput>();
        var workstationAssignments = workstationAllocator.Allocate(effectiveInput, effectiveRestDays, shiftAssignments, staffingGaps);

        // 阶段四：班中休息分配（30 分钟固定休息：错峰 → 借调 → 告警）
        var breakAllocator = new BreakAllocator();
        var breakIssues = new List<ScheduleIssueOutput>();
        var breakAssignments = breakAllocator.Allocate(effectiveInput, shiftAssignments, workstationAssignments, breakIssues);

        // 生成日汇总
        var daySummaries = BuildDaySummaries(effectiveInput, effectiveRestDays, shiftAssignments, workstationAssignments);

        // 合规检查（合并岗位缺口、休息告警与合规违规）
        var complianceIssues = BuildComplianceIssues(effectiveInput, effectiveRestDays, shiftAssignments, workstationAssignments, daySummaries);
        var issues = staffingGaps.Concat(breakIssues).Concat(complianceIssues).ToList();

        // 需求覆盖统计（按最少人数口径）
        var demandCoverage = ComputeDemandCoverage(effectiveInput, workstationAssignments, generatedTemplates.Count);

        return new SchedulingOutput(
            restDays,
            shiftAssignments,
            workstationAssignments,
            breakAssignments,
            daySummaries,
            issues,
            demandCoverage);
    }

    /// <summary>
    /// 需求覆盖统计：按营业日口径（某天凌晨时段按前一天类型）计算周期内
    /// 最少/最好需求人·时，以及工作站分配实际覆盖的需求人·时与缺口人·时。
    /// 覆盖按分配记录的实际日历日期归属（跨天班次午夜回绕部分计入次日）。
    /// </summary>
    private static DemandCoverageStats ComputeDemandCoverage(
        SchedulingInput input,
        IReadOnlyList<WorkstationAssignment> workstationAssignments,
        int demandShiftCount)
    {
        var dayTypeByDate = input.DateParameters.ToDictionary(d => d.WorkDate, d => d.DayType);
        var shiftById = input.ShiftTemplates.ToDictionary(s => s.Id);
        var demandMinHours = 0m;
        var demandIdealHours = 0m;
        var coveredHours = 0m;
        var gapHours = 0m;

        var periodDates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();
        var firstDate = periodDates.Count > 0 ? periodDates[0].WorkDate : input.StartDate;
        var lastDate = periodDates.Count > 0 ? periodDates[^1].WorkDate : input.EndDate;

        // 按日历日统计需求与覆盖；周期首日的凌晨时段归属上一营业日（不计入），
        // 周期最后一天的次日凌晨（归属最后营业日）在循环外补计。
        foreach (var date in periodDates)
        {
            var prevType = dayTypeByDate.GetValueOrDefault(date.WorkDate.AddDays(-1)) ?? date.DayType;
            var dailyDemand = input.StaffingRequirements
                .Where(r => r.DayType == (r.TimeSlot < TimeSpan.FromHours(6) ? prevType : date.DayType))
                .Where(r => !(r.TimeSlot < TimeSpan.FromHours(6) && date.WorkDate == firstDate))
                .GroupBy(r => (r.WorkstationId, r.TimeSlot))
                .ToDictionary(
                    g => g.Key,
                    g => (Min: g.Max(x => x.RequiredCount), Ideal: g.Max(x => x.IdealCount > 0 ? x.IdealCount : x.RequiredCount)));

            var coverage = new Dictionary<(long WorkstationId, TimeSpan Slot), int>();
            foreach (var a in workstationAssignments)
            {
                var calendarDate = shiftById.TryGetValue(a.ShiftTemplateId, out var template)
                    ? SchedulingTimeHelper.SlotCalendarDate(a.TimeSlot, template.StartTime, a.WorkDate)
                    : a.WorkDate;
                if (calendarDate != date.WorkDate)
                {
                    continue;
                }

                var key = (a.WorkstationId, a.TimeSlot);
                coverage[key] = coverage.GetValueOrDefault(key) + 1;
            }

            foreach (var kv in dailyDemand)
            {
                Accumulate(kv.Key, kv.Value.Min, kv.Value.Ideal, coverage.GetValueOrDefault(kv.Key));
            }
        }

        // 补计周期最后一天的次日凌晨（跨天班次午夜回绕部分），归属最后营业日的需求
        if (periodDates.Count > 0)
        {
            var lastType = dayTypeByDate.GetValueOrDefault(lastDate) ?? "WORKDAY";
            var tailDate = lastDate.AddDays(1);
            var tailDemand = input.StaffingRequirements
                .Where(r => r.TimeSlot < TimeSpan.FromHours(6) && r.DayType == lastType)
                .GroupBy(r => (r.WorkstationId, r.TimeSlot))
                .ToDictionary(
                    g => g.Key,
                    g => (Min: g.Max(x => x.RequiredCount), Ideal: g.Max(x => x.IdealCount > 0 ? x.IdealCount : x.RequiredCount)));

            var tailCoverage = new Dictionary<(long WorkstationId, TimeSpan Slot), int>();
            foreach (var a in workstationAssignments)
            {
                var calendarDate = shiftById.TryGetValue(a.ShiftTemplateId, out var template)
                    ? SchedulingTimeHelper.SlotCalendarDate(a.TimeSlot, template.StartTime, a.WorkDate)
                    : a.WorkDate;
                if (calendarDate != tailDate)
                {
                    continue;
                }

                var key = (a.WorkstationId, a.TimeSlot);
                tailCoverage[key] = tailCoverage.GetValueOrDefault(key) + 1;
            }

            foreach (var kv in tailDemand)
            {
                Accumulate(kv.Key, kv.Value.Min, kv.Value.Ideal, tailCoverage.GetValueOrDefault(kv.Key));
            }
        }

        void Accumulate((long WorkstationId, TimeSpan Slot) key, int min, int ideal, int actual)
        {
            demandMinHours += min * 0.5m;
            demandIdealHours += ideal * 0.5m;
            coveredHours += Math.Min(actual, min) * 0.5m;
            gapHours += Math.Max(0, min - actual) * 0.5m;
        }

        var coveragePct = demandMinHours > 0 ? (int)Math.Round(coveredHours * 100m / demandMinHours) : 100;
        return new DemandCoverageStats(
            Math.Round(demandMinHours, 1),
            Math.Round(demandIdealHours, 1),
            Math.Round(coveredHours, 1),
            Math.Round(gapHours, 1),
            Math.Clamp(coveragePct, 0, 100),
            demandShiftCount);
    }

    private async Task<SchedulingInput> BuildInputAsync(
        long storeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1)
            .Select(x => new EmployeeInput(
                x.Id,
                x.EmployeeNo,
                x.Name,
                x.Department,
                x.PrimaryPosition,
                x.MaxWeeklyHours,
                x.IsParttime))
            .ToListAsync(cancellationToken);

        // 3.11 修复：技能只取本店员工，避免跨店全量拉取
        var storeEmployeeIds = employees.Select(x => x.Id).ToHashSet();
        var skills = await _dbContext.EmployeeSkills
            .AsNoTracking()
            .Where(x => x.Status == 1 && x.SkillScore > 0 && storeEmployeeIds.Contains(x.EmployeeId))
            .Select(x => new SkillInput(x.EmployeeId, x.WorkstationId, x.SkillScore, x.IsPrimarySkill))
            .ToListAsync(cancellationToken);

        var dateParameters = await _dbContext.DateParameters
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.WorkDate >= startDate && x.WorkDate <= endDate)
            .Select(x => new DateParameterInput(
                x.WorkDate,
                x.WeekDay,
                x.DayType,
                x.IsLegalHoliday,
                x.IsHolidayEve))
            .ToListAsync(cancellationToken);

        var workstations = await _dbContext.Workstations
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1)
            .Select(x => new { x.Id, x.IsLowSkill })
            .ToListAsync(cancellationToken);

        var shiftTemplates = await _dbContext.ShiftTemplates
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1)
            .Select(x => new { x.Id, x.Code, x.Name, x.StartTime, x.EndTime, x.IsCrossDay, x.Priority })
            .ToListAsync(cancellationToken);

        // 3.11 修复：班次-工作站关联只取本店班次，避免跨店全量拉取
        var shiftTemplateIds = shiftTemplates.Select(x => x.Id).ToHashSet();
        var shiftWorkstations = await _dbContext.ShiftWorkstations
            .AsNoTracking()
            .Where(x => shiftTemplateIds.Contains(x.ShiftTemplateId))
            .ToListAsync(cancellationToken);

        var shiftInputs = shiftTemplates
            .Select(s => new ShiftTemplateInput(
                s.Id,
                s.Code,
                s.Name,
                s.StartTime,
                s.EndTime,
                s.IsCrossDay,
                s.Priority,
                shiftWorkstations.Where(x => x.ShiftTemplateId == s.Id).Select(x => x.WorkstationId).ToList()))
            .ToList();

        var staffingRequirements = await _dbContext.StaffingRequirements
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => new StaffingRequirementInput(
                x.DayType,
                x.WorkstationId,
                x.TimeSlot,
                x.RequiredCount,
                x.IdealCount > 0 ? x.IdealCount : x.RequiredCount))
            .ToListAsync(cancellationToken);

        // 读取已批准请假：请假期间该员工强制休息，不参与排班
        var approvedLeaves = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == "APPROVED" &&
                        x.StartDate <= endDate && x.EndDate >= startDate)
            .Select(x => new ApprovedLeaveInput(x.EmployeeId, x.StartDate, x.EndDate))
            .ToListAsync(cancellationToken);

        // 高峰禁休时段（无配置时算法使用默认 20:00-22:00）
        // 排序放客户端：SQLite 无法翻译 TimeSpan 的 ORDER BY，且数据量极小（每店最多 10 条）
        var peakRestrictedHours = (await _dbContext.PeakRestrictedHours
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1)
            .Select(x => new PeakRestrictedHourInput(x.StartTime, x.EndTime))
            .ToListAsync(cancellationToken))
            .OrderBy(x => x.StartTime)
            .ToList();

        var rules = await _dbContext.RuleConfigs
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == 1)
            .ToDictionaryAsync(x => x.RuleKey, x => x.RuleValue, cancellationToken);

        var defaultMonthlyRestDays = GetRuleInt(rules, "default_monthly_rest_days", 4);
        var maxWeeklyHours = GetRuleDecimal(rules, "max_weekly_hours", 48m);
        var maxConsecutiveWorkDays = GetRuleInt(rules, "max_consecutive_work_days", 6);
        var minRestHoursAfterNightShift = GetRuleInt(rules, "min_rest_hours_after_night_shift", 10);

        var lowSkillWorkstationIds = workstations
            .Where(x => x.IsLowSkill == 1)
            .ToDictionary(x => x.Id, _ => true);

        return new SchedulingInput(
            storeId,
            startDate,
            endDate,
            employees,
            skills,
            dateParameters,
            shiftInputs,
            staffingRequirements,
            approvedLeaves,
            peakRestrictedHours,
            lowSkillWorkstationIds,
            defaultMonthlyRestDays,
            maxWeeklyHours,
            maxConsecutiveWorkDays,
            minRestHoursAfterNightShift);
    }

    /// <summary>
    /// 将已批准请假的日期合并进休息日集合，确保请假员工在请假期间不被排班。
    /// </summary>
    private static IReadOnlyList<RestDayAssignment> MergeApprovedLeaves(
        IReadOnlyList<RestDayAssignment> restDays,
        SchedulingInput input)
    {
        var combined = restDays.ToList();

        foreach (var leave in input.ApprovedLeaves)
        {
            var from = leave.StartDate < input.StartDate ? input.StartDate : leave.StartDate;
            var to = leave.EndDate > input.EndDate ? input.EndDate : leave.EndDate;

            for (var d = from; d <= to; d = d.AddDays(1))
            {
                combined.Add(new RestDayAssignment(leave.EmployeeId, d));
            }
        }

        return combined;
    }

    private static IReadOnlyList<DaySummaryOutput> BuildDaySummaries(
        SchedulingInput input,
        IReadOnlyList<RestDayAssignment> restDays,
        IReadOnlyList<ShiftAssignment> shiftAssignments,
        IReadOnlyList<WorkstationAssignment> workstationAssignments)
    {
        var summaries = new List<DaySummaryOutput>();
        var shiftById = input.ShiftTemplates.ToDictionary(x => x.Id);
        var shiftMap = shiftAssignments
            .GroupBy(x => (x.EmployeeId, x.WorkDate))
            .ToDictionary(g => g.Key, g => g.First());
        var restSet = restDays.ToHashSet();

        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .Select(x => x.WorkDate)
            .ToList();

        foreach (var employee in input.Employees)
        {
            foreach (var date in dates)
            {
                var isRest = restSet.Contains(new RestDayAssignment(employee.Id, date));
                if (isRest)
                {
                    summaries.Add(new DaySummaryOutput(employee.Id, date, 1, null, null, null, 0m, null));
                    continue;
                }

                if (!shiftMap.TryGetValue((employee.Id, date), out var shift) ||
                    !shiftById.TryGetValue(shift.ShiftTemplateId, out var template))
                {
                    // 未安排班次但也不是"正式休息日"的空闲日：补记为休息，
                    // 避免视图出现既无班次也无休息标记的空白格（语义 = 休息）。
                    summaries.Add(new DaySummaryOutput(employee.Id, date, 1, null, null, null, 0m, null));
                    continue;
                }

                var covered = workstationAssignments
                    .Where(a => a.EmployeeId == employee.Id && a.WorkDate == date)
                    .Select(a => a.WorkstationId)
                    .Distinct()
                    .Select(id => id.ToString())
                    .ToList();

                summaries.Add(new DaySummaryOutput(
                    employee.Id,
                    date,
                    0,
                    template.Id,
                    template.StartTime,
                    template.EndTime,
                    SchedulingTimeHelper.GetShiftHours(template.StartTime, template.EndTime, template.IsCrossDay),
                    covered.Count == 0 ? null : string.Join(",", covered)));
            }
        }

        return summaries;
    }

    private static IReadOnlyList<ScheduleIssueOutput> BuildComplianceIssues(
        SchedulingInput input,
        IReadOnlyList<RestDayAssignment> restDays,
        IReadOnlyList<ShiftAssignment> shiftAssignments,
        IReadOnlyList<WorkstationAssignment> workstationAssignments,
        IReadOnlyList<DaySummaryOutput> daySummaries)
    {
        var issues = new List<ScheduleIssueOutput>();
        var shiftById = input.ShiftTemplates.ToDictionary(x => x.Id);
        var employeeById = input.Employees.ToDictionary(x => x.Id);

        // 1. 工时超限检查（按自然周分组：周一~周日为一周，逐周判断是否超过周上限）
        var weeklyHoursByEmployeeAndWeek = daySummaries
            .Where(x => x.IsRestDay == 0)
            .GroupBy(x => new { x.EmployeeId, WeekStart = GetWeekStart(x.WorkDate) })
            .ToDictionary(g => g.Key, g => g.Sum(x => x.WorkHours));

        foreach (var entry in weeklyHoursByEmployeeAndWeek)
        {
            if (entry.Value > input.MaxWeeklyHours && employeeById.TryGetValue(entry.Key.EmployeeId, out var emp))
            {
                issues.Add(new ScheduleIssueOutput(
                    "OVERTIME",
                    "WARN",
                    entry.Key.WeekStart,
                    null,
                    entry.Key.EmployeeId,
                    null,
                    $"员工 {emp.Name} 在 {entry.Key.WeekStart:yyyy-MM-dd} 这一周总工时 {entry.Value:0.##} 超过上限 {input.MaxWeeklyHours:0.##}"));
            }
        }

        // 2. 连续工作超限检查
        var sortedShifts = shiftAssignments
            .OrderBy(x => x.WorkDate)
            .ToList();

        foreach (var employee in input.Employees)
        {
            var employeeShifts = sortedShifts
                .Where(x => x.EmployeeId == employee.Id)
                .Select(x => x.WorkDate)
                .Distinct()
                .ToList();

            var streak = 0;
            DateOnly? prev = null;
            foreach (var date in employeeShifts)
            {
                if (prev is not null && date == prev.Value.AddDays(1))
                {
                    streak++;
                }
                else
                {
                    streak = 1;
                }

                if (streak > input.MaxConsecutiveWorkDays)
                {
                    issues.Add(new ScheduleIssueOutput(
                        "CONSECUTIVE_WORK",
                        "WARN",
                        date,
                        null,
                        employee.Id,
                        null,
                        $"员工 {employee.Name} 从 {date.AddDays(-(streak - 1)):yyyy-MM-dd} 起连续工作 {streak} 天超过上限 {input.MaxConsecutiveWorkDays}"));
                    break;
                }

                prev = date;
            }
        }

        // 3. 工作站分配技能不匹配检查
        foreach (var assignment in workstationAssignments)
        {
            var hasSkill = input.Skills.Any(s =>
                s.EmployeeId == assignment.EmployeeId &&
                s.WorkstationId == assignment.WorkstationId &&
                s.SkillScore > 0);

            if (!hasSkill)
            {
                issues.Add(new ScheduleIssueOutput(
                    "SKILL_MISMATCH",
                    "ERROR",
                    assignment.WorkDate,
                    assignment.TimeSlot,
                    assignment.EmployeeId,
                    assignment.WorkstationId,
                    $"员工已分配到无技能的工作站 {assignment.WorkstationId}"));
            }
        }

        return issues;
    }

    /// <summary>返回日期所在自然周的周一（周为一周起点）。</summary>
    private static DateOnly GetWeekStart(DateOnly date)
    {
        // DayOfWeek: Sunday=0 ... Saturday=6；转成 周一=0 ... 周日=6
        var dayOffset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-dayOffset);
    }

    private static int GetRuleInt(IReadOnlyDictionary<string, string> rules, string key, int defaultValue)
        => rules.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;

    private static decimal GetRuleDecimal(IReadOnlyDictionary<string, string> rules, string key, decimal defaultValue)
        => rules.TryGetValue(key, out var value) && decimal.TryParse(value, out var result) ? result : defaultValue;
}