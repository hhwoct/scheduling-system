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
        // 输入净化：闭店空窗（打烊后 06:00 ~ 开门前 13:00）不参与任何需求计算。
        // 班表时间轴为 13:00 开门 → 次日 06:00 打烊（05:30 为最后一格），06:00 起属于闭店时段；
        // 若人数需求数据误配了该时段需求（历史曾导致 06:00 出现 D 临时班次、
        // 界面不可见的幽灵时段），在此统一剔除，防止复发。
        input = input with
        {
            StaffingRequirements = input.StaffingRequirements
                .Where(r => r.TimeSlot < TimeSpan.FromHours(6) || r.TimeSlot >= TimeSpan.FromHours(13))
                .ToList()
        };

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

        // 阶段三：工作站分配（缺口不在此报告——兜底填充后按最终状态统一报告）
        var workstationAssignments = workstationAllocator.Allocate(effectiveInput, effectiveRestDays, shiftAssignments, null);

        // 阶段四：班中休息分配（30 分钟固定休息：错峰 → 借调 → 告警）
        var breakAllocator = new BreakAllocator();
        var breakIssues = new List<ScheduleIssueOutput>();
        var breakAssignments = breakAllocator.Allocate(effectiveInput, shiftAssignments, workstationAssignments, breakIssues);

        // 阶段四.5：剩余缺口兜底填充（全职优先，兼职填充剩余格子）
        var shiftAssignmentsFinal = shiftAssignments.ToList();
        var workstationAssignmentsFinal = workstationAssignments.ToList();
        var residualTemplates = new List<ShiftTemplateInput>();
        var remainingResidual = ResidualGapFiller.Fill(effectiveInput, effectiveRestDays, shiftAssignmentsFinal, workstationAssignmentsFinal, residualTemplates);
        // 缺口报告以兜底填充后的最终状态为准
        var staffingGaps = ResidualGapFiller.BuildGapIssues(effectiveInput, remainingResidual);
        var summaryInput = residualTemplates.Count == 0
            ? effectiveInput
            : effectiveInput with { ShiftTemplates = effectiveInput.ShiftTemplates.Concat(residualTemplates).ToList() };

        // 生成日汇总
        var daySummaries = BuildDaySummaries(summaryInput, effectiveRestDays, shiftAssignmentsFinal, workstationAssignmentsFinal);

        // 合规检查（合并岗位缺口、休息告警与合规违规）
        var complianceIssues = BuildComplianceIssues(summaryInput, effectiveRestDays, shiftAssignmentsFinal, workstationAssignmentsFinal, daySummaries);
        var issues = staffingGaps.Concat(breakIssues).Concat(complianceIssues).ToList();

        // 需求覆盖统计（按最少人数口径）
        var demandCoverage = ComputeDemandCoverage(effectiveInput, workstationAssignmentsFinal, generatedTemplates.Count);

        return new SchedulingOutput(
            restDays,
            shiftAssignmentsFinal,
            workstationAssignmentsFinal,
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
            .Select(x => new { x.Id, x.Code, x.Name, x.IsLowSkill })
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
        var maxConsecutiveWorkDays = GetRuleInt(rules, "max_consecutive_work_days", 6);
        var minRestHoursAfterNightShift = GetRuleInt(rules, "min_rest_hours_after_night_shift", 10);
        // 正式员工每日最低工时：上班当天工时不得低于该值（0 表示不限制）
        var minDailyWorkHours = GetRuleDecimal(rules, "min_daily_work_hours", 6.5m);
        // 单日最大工时：可为不同岗位单独设置（JSON：{"default":12,"保洁":10}）；纯数字视为全部岗位默认值（0 表示不限制）
        var maxDailyWorkHours = 12m;
        var maxDailyWorkHoursByDepartment = new Dictionary<string, decimal>();
        var dailyRuleValue = rules.GetValueOrDefault("max_daily_work_hours") ?? "12";
        if (decimal.TryParse(dailyRuleValue, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var plainDaily))
        {
            maxDailyWorkHours = plainDaily;
        }
        else
        {
            try
            {
                var dailyMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(dailyRuleValue);
                if (dailyMap is not null && dailyMap.Count > 0)
                {
                    if (dailyMap.TryGetValue("default", out var def))
                    {
                        maxDailyWorkHours = def;
                    }

                    foreach (var (dept, hours) in dailyMap)
                    {
                        if (dept != "default")
                        {
                            maxDailyWorkHoursByDepartment[dept] = hours;
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // 非法 JSON 回退默认 12
            }
        }

        // 偏好学习（feature/schedule-pref-learning）：软排序因子。
        // 权重 0 = 关闭；>0 时在技能分排序中作为次级键（技能分相同/接近时贴合店长历史习惯）。
        // 班次偏好：shift_code 维度；工作站偏好：workstation 维度；休息样本两列皆 NULL 不参与分配排序。
        var preferenceWeight = GetRuleDecimal(rules, "preference_learning_weight", 0m);
        var preferenceRows = preferenceWeight > 0m
            ? await _dbContext.EmployeePreferences.AsNoTracking()
                .Where(x => x.StoreId == storeId && x.Freq > 0)
                .ToListAsync(cancellationToken)
            : null;

        IReadOnlyDictionary<(long EmployeeId, string DayType), IReadOnlyDictionary<string, int>>? preferenceShiftScores = null;
        IReadOnlyDictionary<(long EmployeeId, string DayType), IReadOnlyDictionary<long, int>>? preferenceWsScores = null;
        IReadOnlyDictionary<(long EmployeeId, string DayType), int>? preferenceRestScores = null;
        if (preferenceRows is not null)
        {
            preferenceShiftScores = preferenceRows
                .Where(x => x.ShiftCode != null)
                .GroupBy(x => (x.EmployeeId, x.DayType))
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyDictionary<string, int>)g.ToDictionary(x => x.ShiftCode!, x => x.Freq));

            preferenceWsScores = preferenceRows
                .Where(x => x.WorkstationId != null)
                .GroupBy(x => (x.EmployeeId, x.DayType))
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyDictionary<long, int>)g.ToDictionary(x => x.WorkstationId!.Value, x => x.Freq));

            // 休息偏好：shift_code 与 workstation_id 皆 NULL 的样本（店长习惯让该员工某类型日休息）
            preferenceRestScores = preferenceRows
                .Where(x => x.ShiftCode == null && x.WorkstationId == null)
                .GroupBy(x => (x.EmployeeId, x.DayType))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Freq));
        }

        var lowSkillWorkstationIds = workstations
            .Where(x => x.IsLowSkill == 1)
            .ToDictionary(x => x.Id, _ => true);

        // 缺口仅提醒工作站（如工程维修岗）：岗位缺口不逐时段记录，整周期只出一条汇总提醒。
        // 按工作站编码识别，如需扩展（如网络维护岗），在此追加编码即可。
        var warnOnlyGapWorkstations = workstations
            .Where(x => x.Code == "ENGINEERING")
            .ToDictionary(x => x.Id, x => x.Name);

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
            warnOnlyGapWorkstations,
            defaultMonthlyRestDays,
            maxConsecutiveWorkDays,
            minRestHoursAfterNightShift,
            minDailyWorkHours,
            preferenceShiftScores,
            preferenceWsScores,
            preferenceRestScores,
            preferenceWeight,
            maxDailyWorkHours,
            maxDailyWorkHoursByDepartment);
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
        // 一天可能有多个互不重叠的班次（模板班次 + 按需补班 D 班次）：
        // 不能只取第一个班次，否则会出现"只显示凌晨 30 分钟短班、休息却在晚上"的错位展示，
        // 且工时被少算。按 (员工, 日期) 分组后合并（见下方主班次/总工时逻辑）。
        var shiftsByEmployeeDate = shiftAssignments
            .GroupBy(x => (x.EmployeeId, x.WorkDate))
            .ToDictionary(g => g.Key, g => g.ToList());
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

                var dayShifts = shiftsByEmployeeDate.GetValueOrDefault((employee.Id, date)) ?? new List<ShiftAssignment>();
                var resolvable = dayShifts
                    .Where(s => shiftById.TryGetValue(s.ShiftTemplateId, out _))
                    .ToList();

                if (resolvable.Count == 0)
                {
                    // 未安排班次但也不是"正式休息日"的空闲日：补记为休息，
                    // 避免视图出现既无班次也无休息标记的空白格（语义 = 休息）。
                    summaries.Add(new DaySummaryOutput(employee.Id, date, 1, null, null, null, 0m, null));
                    continue;
                }

                // 主班次 = 当天工时最长的班次：起止时间与班次编码以它为准（休息通常落在其时段内）
                var mainShift = resolvable
                    .OrderByDescending(s =>
                    {
                        var t = shiftById[s.ShiftTemplateId];
                        return SchedulingTimeHelper.GetShiftHours(t.StartTime, t.EndTime, t.IsCrossDay);
                    })
                    .ThenBy(s => s.ShiftTemplateId)
                    .First();
                var mainTemplate = shiftById[mainShift.ShiftTemplateId];

                // 总工时 = 当天全部班次工时之和（含短班，避免汇总少算）
                var totalHours = resolvable.Sum(s =>
                {
                    var t = shiftById[s.ShiftTemplateId];
                    return SchedulingTimeHelper.GetShiftHours(t.StartTime, t.EndTime, t.IsCrossDay);
                });

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
                    mainTemplate.Id,
                    mainTemplate.StartTime,
                    mainTemplate.EndTime,
                    totalHours,
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
            if (employeeById.TryGetValue(entry.Key.EmployeeId, out var emp) && entry.Value > emp.MaxWeeklyHours)
            {
                issues.Add(new ScheduleIssueOutput(
                    "OVERTIME",
                    "WARN",
                    entry.Key.WeekStart,
                    null,
                    entry.Key.EmployeeId,
                    null,
                    $"员工 {emp.Name} 在 {entry.Key.WeekStart:yyyy-MM-dd} 这一周总工时 {entry.Value:0.##} 超过上限 {emp.MaxWeeklyHours:0.##}"));
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

        // 4. 正式员工每日最低工时检查（上班当天工时不得低于规则值，0 表示不限制）
        if (input.MinDailyWorkHours > 0)
        {
            foreach (var summary in daySummaries.Where(x =>
                         x.IsRestDay == 0 && x.WorkHours > 0m && x.WorkHours < input.MinDailyWorkHours))
            {
                if (employeeById.TryGetValue(summary.EmployeeId, out var emp) && emp.IsParttime == 0)
                {
                    issues.Add(new ScheduleIssueOutput(
                        "MIN_DAILY_HOURS",
                        "WARN",
                        summary.WorkDate,
                        null,
                        summary.EmployeeId,
                        null,
                        $"员工 {emp.Name} 在 {summary.WorkDate:yyyy-MM-dd} 当日工时 {summary.WorkHours:0.##}h 低于正式员工每日最低 {input.MinDailyWorkHours:0.##}h"));
                }
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