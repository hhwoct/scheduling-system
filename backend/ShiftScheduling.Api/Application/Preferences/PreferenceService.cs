using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.RuleConfigs;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.Preferences;

/// <summary>
/// 排班偏好学习服务（feature/schedule-pref-learning）：
/// 把店长「手动调整后发布」的排班视为认可样本，聚合为员工偏好统计，
/// 供排班算法作为软排序因子；并提供仪表盘统计（贴合率/覆盖率/趋势）。
///
/// 学习口径：
/// - 样本 = 已发布(PUBLISHED)计划的明细：schedule_summaries（班次/休息）+ schedule_results（工作站）
/// - 全量重建时**排除最新一期**用于聚合偏好，最新一期专门用于评估「贴合率」
///   （历史偏好预测本期的匹配度，避免本期自身抬高指标）
/// - 三个样本维度：班次偏好（shift_code）、工作站偏好（workstation_id）、休息偏好（两者皆 NULL）
/// </summary>
public sealed class PreferenceService : IPreferenceService
{
    private const int MaxLearningPeriods = 12;
    private const int MinSamplesForCoverage = 3;
    private const string RuleKeyWeight = "preference_learning_weight";

    /// <summary>纠错信号权重：店长手动调整 1 次 = 认可样本的 2 倍（主动纠错比被动认可信号更强）。</summary>
    private const int CorrectionWeight = 2;

    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public PreferenceService(ShiftSchedulingDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task<PreferenceStats> RebuildAsync(long storeId, CancellationToken cancellationToken)
    {
        var weight = await GetWeightAsync(storeId, cancellationToken);

        var publishedPlans = await _dbContext.SchedulePlans
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status == "PUBLISHED" && x.PublishedAt != null)
            .OrderBy(x => x.PublishedAt)
            .Take(MaxLearningPeriods)
            .Select(x => new { x.Id, x.PlanName, x.PublishedAt })
            .ToListAsync(cancellationToken);

        if (publishedPlans.Count == 0)
        {
            return new PreferenceStats(0, 0, 0m, 0m, 0, weight);
        }

        // 学习期 = 除最新一期外的历史；评估期 = 最新一期
        var learningPlans = publishedPlans.Take(Math.Max(0, publishedPlans.Count - 1)).ToList();
        var evalPlan = publishedPlans[^1];

        // day_type 内存映射（避免逐行查库）
        var dayTypeByDate = await _dbContext.DateParameters.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.WorkDate, x => x.DayType, cancellationToken);

        var shiftCodes = await _dbContext.ShiftTemplates.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        // ========== 聚合学习期 ==========
        // 兼职（is_parttime=1）无「休息」语义：空闲日 ≠ 店长安排休息，
        // 其休息样本为伪信号（会把「没排班」学成「店长习惯让他休」），
        // 故兼职的休息样本不统计；班次/工作站偏好保留（店长对兼职的使用习惯仍有价值）。
        var partTimeEmployeeIds = (await _dbContext.Employees.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsParttime == 1)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var agg = new Dictionary<(long EmpId, string DayType, string? ShiftCode, long? WsId), int>();
        var sampleDays = 0;

        foreach (var plan in learningPlans)
        {
            // 班次/休息样本
            var summaries = await _dbContext.ScheduleSummaries.AsNoTracking()
                .Where(x => x.PlanId == plan.Id && x.StoreId == storeId)
                .Select(x => new { x.EmployeeId, x.WorkDate, x.IsRestDay, x.ShiftTemplateId })
                .ToListAsync(cancellationToken);

            foreach (var s in summaries)
            {
                var dayType = dayTypeByDate.GetValueOrDefault(s.WorkDate) ?? "WORKDAY";
                if (s.IsRestDay == 1)
                {
                    // 休息样本（兼职排除：其空闲日非店长安排，属伪信号）
                    if (partTimeEmployeeIds.Contains(s.EmployeeId))
                    {
                        continue;
                    }
                    var restKey = (s.EmployeeId, dayType, (string?)null, (long?)null);
                    agg[restKey] = agg.GetValueOrDefault(restKey) + 1;
                }
                else
                {
                    var shiftCode = s.ShiftTemplateId != null && shiftCodes.TryGetValue(s.ShiftTemplateId.Value, out var c) ? c : null;
                    if (shiftCode is not null)
                    {
                        var shiftKey = (s.EmployeeId, dayType, shiftCode, (long?)null);
                        agg[shiftKey] = agg.GetValueOrDefault(shiftKey) + 1;
                    }
                }
            }

            // 工作站样本
            var wsAssignments = await _dbContext.ScheduleResults.AsNoTracking()
                .Where(x => x.PlanId == plan.Id && x.StoreId == storeId && x.WorkstationId != null)
                .Select(x => new { x.EmployeeId, x.WorkDate, x.WorkstationId })
                .ToListAsync(cancellationToken);

            foreach (var a in wsAssignments)
            {
                var dayType = dayTypeByDate.GetValueOrDefault(a.WorkDate) ?? "WORKDAY";
                var wsKey = (a.EmployeeId, dayType, (string?)null, a.WorkstationId);
                agg[wsKey] = agg.GetValueOrDefault(wsKey) + 1;
            }

            // ===== 纠错方向学习（增强）：调整明细 =====
            // 店长的每次手动调整都是主动纠错信号，权重 = 认可样本的 2 倍。
            // MOVE_SEGMENT → 目标工作站偏好；SET_REST → 休息偏好（兼职排除）；
            // ADJUST → 班次/工作站偏好；SET_WORK（恢复上班）为需求信号，不学。
            var corrections = await _dbContext.ScheduleAdjustments.AsNoTracking()
                .Where(x => x.PlanId == plan.Id && x.StoreId == storeId)
                .Select(x => new { x.EmployeeId, x.WorkDate, x.ActionType, x.BeforeJson, x.AfterJson })
                .ToListAsync(cancellationToken);

            foreach (var adj in corrections)
            {
                var dayType = dayTypeByDate.GetValueOrDefault(adj.WorkDate ?? default) ?? "WORKDAY";
                if (adj.WorkDate is null || adj.EmployeeId is null)
                {
                    continue;
                }

                switch (adj.ActionType)
                {
                    case "MOVE_SEGMENT":
                        {
                            var after = TryParseJson(adj.AfterJson);
                            if (after?.WorkstationId is not null)
                            {
                                var key = (adj.EmployeeId.Value, dayType, (string?)null, (long?)after.WorkstationId);
                                agg[key] = agg.GetValueOrDefault(key) + CorrectionWeight;
                            }
                            break;
                        }
                    case "SET_REST":
                        {
                            // 兼职休息纠错同为伪信号（空闲日），排除
                            if (!partTimeEmployeeIds.Contains(adj.EmployeeId.Value))
                            {
                                var key = (adj.EmployeeId.Value, dayType, (string?)null, (long?)null);
                                agg[key] = agg.GetValueOrDefault(key) + CorrectionWeight;
                            }
                            break;
                        }
                    case "ADJUST":
                        {
                            var after = TryParseJson(adj.AfterJson);
                            if (after?.ShiftTemplateId is not null &&
                                shiftCodes.TryGetValue(after.ShiftTemplateId.Value, out var code))
                            {
                                var shiftKey = (adj.EmployeeId.Value, dayType, code, (long?)null);
                                agg[shiftKey] = agg.GetValueOrDefault(shiftKey) + CorrectionWeight;
                            }
                            if (after?.WorkstationId is not null)
                            {
                                var wsKey = (adj.EmployeeId.Value, dayType, (string?)null, (long?)after.WorkstationId);
                                agg[wsKey] = agg.GetValueOrDefault(wsKey) + CorrectionWeight;
                            }
                            break;
                        }
                }
            }

            sampleDays += summaries.Count;
        }

        // ========== 写偏好表（事务：先清后插，幂等自愈） ==========
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.EmployeePreferences
            .Where(x => x.StoreId == storeId)
            .ExecuteDeleteAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var (key, freq) in agg)
        {
            _dbContext.EmployeePreferences.Add(new EmployeePreferenceEntity
            {
                StoreId = storeId,
                EmployeeId = key.EmpId,
                DayType = key.DayType,
                ShiftCode = key.ShiftCode,
                WorkstationId = key.WsId,
                Freq = freq,
                LastSeenAt = now
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        // ========== 评估期贴合率：班次/休息组合在历史偏好中出现过的比例 ==========
        var evalSummaries = await _dbContext.ScheduleSummaries.AsNoTracking()
            .Where(x => x.PlanId == evalPlan.Id && x.StoreId == storeId)
            .Select(x => new { x.EmployeeId, x.WorkDate, x.IsRestDay, x.ShiftTemplateId })
            .ToListAsync(cancellationToken);

        var preferenceSet = new HashSet<(long EmpId, string DayType, string? ShiftCode, long? WsId)>(agg.Keys);

        var matchCount = 0;
        foreach (var s in evalSummaries)
        {
            var dayType = dayTypeByDate.GetValueOrDefault(s.WorkDate) ?? "WORKDAY";
            if (s.IsRestDay == 1)
            {
                if (preferenceSet.Contains((s.EmployeeId, dayType, (string?)null, (long?)null)))
                {
                    matchCount++;
                }
            }
            else
            {
                var shiftCode = s.ShiftTemplateId != null && shiftCodes.TryGetValue(s.ShiftTemplateId.Value, out var c) ? c : null;
                if (shiftCode is not null && preferenceSet.Contains((s.EmployeeId, dayType, shiftCode, (long?)null)))
                {
                    matchCount++;
                }
            }
        }

        var adherence = evalSummaries.Count > 0 ? Math.Round(matchCount * 100m / evalSummaries.Count, 1) : 0m;
        var coverage = await ComputeCoverageAsync(storeId, cancellationToken);

        // ========== 调整量（增强 2）：生成快照 vs 发布最终，员工×日期 维度差异 ==========
        var adjustments = await ComputeAdjustmentsAsync(evalPlan.Id, storeId, cancellationToken);

        // 趋势记录（按 plan 幂等 upsert）
        var trend = await _dbContext.PreferenceTrends
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.PlanId == evalPlan.Id, cancellationToken);
        if (trend is null)
        {
            trend = new PreferenceTrendEntity { StoreId = storeId, PlanId = evalPlan.Id, CreatedAt = now };
            _dbContext.PreferenceTrends.Add(trend);
        }
        trend.PlanName = evalPlan.PlanName;
        trend.PublishedAt = evalPlan.PublishedAt ?? now;
        trend.AdherencePct = adherence;
        trend.CoveragePct = coverage;
        trend.SampleDays = sampleDays;
        trend.Adjustments = adjustments;
        trend.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var rowCount = await _dbContext.EmployeePreferences.CountAsync(x => x.StoreId == storeId, cancellationToken);
        return new PreferenceStats(learningPlans.Count, sampleDays, coverage, adherence, rowCount, weight);
    }

    public async Task<PreferenceStats> GetStatsAsync(long storeId, CancellationToken cancellationToken)
    {
        var weight = await GetWeightAsync(storeId, cancellationToken);
        var rowCount = await _dbContext.EmployeePreferences.CountAsync(x => x.StoreId == storeId, cancellationToken);
        var publishedCount = await _dbContext.SchedulePlans.CountAsync(
            x => x.StoreId == storeId && x.Status == "PUBLISHED" && x.PublishedAt != null, cancellationToken);
        var coverage = await ComputeCoverageAsync(storeId, cancellationToken);

        // 贴合率/样本量以最新趋势记录为准（与 Rebuild 的学习期口径一致）
        var latestTrend = await _dbContext.PreferenceTrends.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderByDescending(x => x.PublishedAt)
            .Select(x => new { x.AdherencePct, x.SampleDays })
            .FirstOrDefaultAsync(cancellationToken);

        return new PreferenceStats(
            Math.Max(0, publishedCount - 1),
            latestTrend?.SampleDays ?? 0,
            coverage,
            latestTrend?.AdherencePct ?? 0m,
            rowCount,
            weight);
    }

    public async Task<IReadOnlyList<PreferenceMatrixItem>> GetMatrixAsync(long storeId, string? dayType, CancellationToken cancellationToken)
    {
        var query = _dbContext.EmployeePreferences.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.WorkstationId != null && x.Freq >= MinSamplesForCoverage);
        if (!string.IsNullOrWhiteSpace(dayType))
        {
            query = query.Where(x => x.DayType == dayType);
        }

        var items = await query
            .Join(_dbContext.Employees, p => p.EmployeeId, e => e.Id, (p, e) => new { p, e })
            .Join(_dbContext.Workstations, x => x.p.WorkstationId, w => w.Id, (x, w) => new { x.p, x.e, w })
            .OrderByDescending(x => x.p.Freq)
            .Take(500)
            .Select(x => new PreferenceMatrixItem(x.e.EmployeeNo, x.e.Name, x.w.Code, x.p.DayType, x.p.Freq))
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task<IReadOnlyList<PreferenceTopItem>> GetTopAsync(long storeId, int limit, CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        var items = await _dbContext.EmployeePreferences.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Join(_dbContext.Employees, p => p.EmployeeId, e => e.Id, (p, e) => new { p, e })
            .OrderByDescending(x => x.p.Freq)
            .Take(safeLimit)
            .Select(x => new PreferenceTopItem(x.e.EmployeeNo, x.e.Name, x.p.DayType, x.p.ShiftCode, x.p.Freq))
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task<IReadOnlyList<PreferenceTrendItem>> GetTrendsAsync(long storeId, CancellationToken cancellationToken)
    {
        return await _dbContext.PreferenceTrends.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.PublishedAt)
            .Select(x => new PreferenceTrendItem(x.PlanId, x.PlanName, x.PublishedAt, x.AdherencePct, x.CoveragePct, x.SampleDays, x.Adjustments))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 计算店长手动调整量：生成快照（GeneratedSummarySnapshot JSON）vs 发布时最终汇总，
    /// 员工×日期 维度上「休息/班次」不一致的条数。快照缺失（旧计划）返回 0。
    /// </summary>
    private async Task<int> ComputeAdjustmentsAsync(long planId, long storeId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SchedulePlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == planId && x.StoreId == storeId, cancellationToken);
        if (plan?.GeneratedSummarySnapshot is null)
        {
            return 0;
        }

        List<SnapshotRow>? snapshot;
        try
        {
            snapshot = System.Text.Json.JsonSerializer.Deserialize<List<SnapshotRow>>(plan.GeneratedSummarySnapshot);
        }
        catch
        {
            return 0;
        }
        if (snapshot is null || snapshot.Count == 0)
        {
            return 0;
        }

        var snapshotMap = snapshot
            .GroupBy(s => (s.EmployeeId, s.WorkDate))
            .ToDictionary(g => g.Key, g => g.First());

        var finalRows = await _dbContext.ScheduleSummaries.AsNoTracking()
            .Where(x => x.PlanId == planId && x.StoreId == storeId)
            .Select(x => new { x.EmployeeId, x.WorkDate, x.IsRestDay, x.ShiftTemplateId })
            .ToListAsync(cancellationToken);

        var adjustments = 0;
        foreach (var f in finalRows)
        {
            if (!snapshotMap.TryGetValue((f.EmployeeId, f.WorkDate), out var s))
            {
                continue; // 快照没有的行（如后期手工新增汇总）不计数
            }

            var sameRest = (f.IsRestDay == 1) == (s.IsRestDay == 1);
            var sameShift = (f.ShiftTemplateId ?? 0) == (s.ShiftId ?? 0);
            if (!sameRest || !sameShift)
            {
                adjustments++;
            }
        }

        return adjustments;
    }

    private sealed record SnapshotRow(long EmployeeId, DateOnly WorkDate, int IsRestDay, long? ShiftId);

    /// <summary>调整明细 JSON 解析（MOVE_SEGMENT 的 after 含 WorkstationId；ADJUST 的 after 含 ShiftTemplateId/WorkstationId）。</summary>
    private sealed record AdjustmentPayload(long? WorkstationId, long? ShiftTemplateId);

    private static AdjustmentPayload? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            long? ws = null;
            long? shift = null;
            if (root.TryGetProperty("WorkstationId", out var wsEl) && wsEl.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                ws = wsEl.GetInt64();
            }
            if (root.TryGetProperty("ShiftTemplateId", out var shiftEl) && shiftEl.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                shift = shiftEl.GetInt64();
            }
            return new AdjustmentPayload(ws, shift);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DemandInsightItem>> GetDemandInsightsAsync(long planId, long storeId, CancellationToken cancellationToken)
    {
        // 需求联动分析：店长反复在某时段「恢复上班」（SET_WORK）或「移入」（MOVE_SEGMENT）
        // → 该 (日期类型, 时段, 工作站) 的需求配置可能不足。
        const int SignalThreshold = 2;

        var adjustments = await _dbContext.ScheduleAdjustments.AsNoTracking()
            .Where(x => x.PlanId == planId && x.StoreId == storeId
                && (x.ActionType == "SET_WORK" || x.ActionType == "MOVE_SEGMENT"))
            .Select(x => new { x.Id, x.EmployeeId, x.WorkDate, x.TimeSlot, x.ActionType, x.AfterJson })
            .ToListAsync(cancellationToken);

        if (adjustments.Count == 0)
        {
            return Array.Empty<DemandInsightItem>();
        }

        var dayTypeByDate = await _dbContext.DateParameters.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.WorkDate, x => x.DayType, cancellationToken);

        // SET_WORK：从 results 反查该员工该时段所在工作站
        var empDateSlots = adjustments
            .Where(a => a.ActionType == "SET_WORK" && a.EmployeeId != null && a.WorkDate != null && a.TimeSlot != null)
            .Select(a => new { a.EmployeeId, a.WorkDate, a.TimeSlot })
            .ToList();

        var wsByKey = new Dictionary<(long EmpId, DateOnly Date, TimeSpan Slot), long>();
        if (empDateSlots.Count > 0)
        {
            var rows = await _dbContext.ScheduleResults.AsNoTracking()
                .Where(x => x.PlanId == planId && x.StoreId == storeId && x.WorkstationId != null)
                .Select(x => new { x.EmployeeId, x.WorkDate, x.TimeSlot, x.WorkstationId })
                .ToListAsync(cancellationToken);
            foreach (var r in rows)
            {
                wsByKey.TryAdd((r.EmployeeId, r.WorkDate, r.TimeSlot), r.WorkstationId!.Value);
            }
        }

        // 聚合信号：(dayType, timeSlot, workstationId) → (count, samples)
        var signals = new Dictionary<(string DayType, TimeSpan Slot, long WsId), (int Count, List<string> Samples)>();
        void AddSignal(string dayType, TimeSpan slot, long wsId, string sample)
        {
            var key = (dayType, slot, wsId);
            if (!signals.TryGetValue(key, out var cur))
            {
                cur = (0, new List<string>());
                signals[key] = cur;
            }
            cur.Count++;
            if (cur.Samples.Count < 3) cur.Samples.Add(sample);
            signals[key] = cur;
        }

        foreach (var adj in adjustments)
        {
            var dayType = adj.WorkDate is null ? "WORKDAY" : dayTypeByDate.GetValueOrDefault(adj.WorkDate.Value, "WORKDAY");
            if (adj.ActionType == "MOVE_SEGMENT")
            {
                var after = TryParseJson(adj.AfterJson);
                if (after?.WorkstationId is not null && adj.TimeSlot is not null)
                {
                    AddSignal(dayType, adj.TimeSlot.Value, after.WorkstationId.Value,
                        $"移入站{after.WorkstationId.Value} {adj.WorkDate:yyyy-MM-dd} {adj.TimeSlot.Value.ToString(@"hh\:mm")}");
                }
            }
            else if (adj.ActionType == "SET_WORK" && adj.EmployeeId != null && adj.WorkDate != null && adj.TimeSlot != null)
            {
                if (wsByKey.TryGetValue((adj.EmployeeId.Value, adj.WorkDate.Value, adj.TimeSlot.Value), out var wsId))
                {
                    AddSignal(dayType, adj.TimeSlot.Value, wsId,
                        $"恢复上班 员工{adj.EmployeeId.Value} {adj.WorkDate:yyyy-MM-dd} {adj.TimeSlot.Value.ToString(@"hh\:mm")}");
                }
            }
        }

        if (signals.Count == 0)
        {
            return Array.Empty<DemandInsightItem>();
        }

        // 与人数需求对比，生成建议（信号 ≥ 阈值）
        var wsCodes = await _dbContext.Workstations.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var demands = await _dbContext.StaffingRequirements.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => new { x.DayType, x.WorkstationId, x.TimeSlot, x.RequiredCount })
            .ToListAsync(cancellationToken);
        var demandByKey = demands
            .GroupBy(d => (d.DayType, d.TimeSlot, d.WorkstationId))
            .ToDictionary(g => g.Key, g => g.First().RequiredCount);

        var insights = new List<DemandInsightItem>();
        foreach (var (key, sig) in signals)
        {
            if (sig.Count < SignalThreshold)
            {
                continue;
            }

            var current = demandByKey.GetValueOrDefault((key.DayType, key.Slot, key.WsId), 0);
            insights.Add(new DemandInsightItem(
                key.DayType,
                key.Slot.ToString(@"hh\:mm"),
                wsCodes.GetValueOrDefault(key.WsId, key.WsId.ToString()),
                current,
                current + 1,
                sig.Count,
                sig.Samples));
        }

        return insights
            .OrderByDescending(x => x.SignalCount)
            .ThenBy(x => x.DayType)
            .ThenBy(x => x.TimeSlot)
            .ToList();
    }

    private async Task<decimal> ComputeCoverageAsync(long storeId, CancellationToken cancellationToken)
    {
        // 覆盖率 = 有足够样本（freq ≥ 3）的全职员工比例（兼职按需排班、样本波动大，不计入）
        var totalEmployees = await _dbContext.Employees.CountAsync(
            x => x.StoreId == storeId && x.Status == 1 && x.IsParttime == 0, cancellationToken);
        if (totalEmployees == 0) return 0m;

        var coveredEmployees = await _dbContext.EmployeePreferences.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Freq >= MinSamplesForCoverage)
            .Join(
                _dbContext.Employees.Where(e => e.IsParttime == 0),
                p => p.EmployeeId,
                e => e.Id,
                (p, _) => p.EmployeeId)
            .Distinct()
            .CountAsync(cancellationToken);

        return Math.Round(coveredEmployees * 100m / totalEmployees, 1);
    }

    private async Task<decimal> GetWeightAsync(long storeId, CancellationToken cancellationToken)
    {
        var rules = await RuleConfigQuery.GetEffectiveAsync(_dbContext, storeId, cancellationToken);
        if (!rules.TryGetValue(RuleKeyWeight, out var ruleValue)
            || !decimal.TryParse(ruleValue, out var w) || w < 0m)
        {
            return 0m; // 默认关闭（开启需将规则设为 > 0）
        }
        return Math.Min(w, 1m);
    }
}
