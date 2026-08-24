using Microsoft.EntityFrameworkCore;
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
    private const string RuleKeyWeight = "preference_weight";

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
                    // 休息样本
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

    private async Task<decimal> ComputeCoverageAsync(long storeId, CancellationToken cancellationToken)
    {
        // 覆盖率 = 有足够样本（freq ≥ 3）的员工比例
        var totalEmployees = await _dbContext.Employees.CountAsync(x => x.StoreId == storeId && x.Status == 1, cancellationToken);
        if (totalEmployees == 0) return 0m;

        var coveredEmployees = await _dbContext.EmployeePreferences.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Freq >= MinSamplesForCoverage)
            .Select(x => x.EmployeeId)
            .Distinct()
            .CountAsync(cancellationToken);

        return Math.Round(coveredEmployees * 100m / totalEmployees, 1);
    }

    private async Task<decimal> GetWeightAsync(long storeId, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.RuleConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.RuleKey == RuleKeyWeight, cancellationToken);
        if (rule is null || !decimal.TryParse(rule.RuleValue, out var w) || w < 0m)
        {
            return 0.3m;
        }
        return Math.Min(w, 1m);
    }
}
