using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Algorithm;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.Schedules;

public sealed class ScheduleService : IScheduleService
{
    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly SchedulingEngine _schedulingEngine;
    private readonly IAuditLogService _auditLogService;

    public ScheduleService(
        ShiftSchedulingDbContext dbContext,
        SchedulingEngine schedulingEngine,
        IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _schedulingEngine = schedulingEngine;
        _auditLogService = auditLogService;
    }

    public async Task<GenerateScheduleResult> GenerateAsync(
        GenerateScheduleRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (request.StartDate > request.EndDate)
        {
            throw new BusinessException("开始日期不能晚于结束日期", "INVALID_DATE_RANGE");
        }

        if (request.EndDate.DayNumber - request.StartDate.DayNumber > 31)
        {
            throw new BusinessException("排班周期不能超过 31 天", "INVALID_DATE_RANGE");
        }

        // 幂等保护：同门店同一周期已发布的排班不允许重复生成；
        // 若存在 DRAFT 草稿计划，则重新生成时在同一事务内级联替换（使算法更新可重新应用）。
        var existingPlan = await _dbContext.SchedulePlans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.StartDate == request.StartDate && x.EndDate == request.EndDate, cancellationToken);
        if (existingPlan is not null && existingPlan.Status == "PUBLISHED")
        {
            throw new BusinessException("该排班周期已存在已发布的排班计划，请勿重复生成", "DUPLICATE_SCHEDULE_PLAN");
        }

        // 修复：先执行新排班生成，再在同一事务内删除旧草稿并写入新数据。
        // 若生成失败，旧草稿保持不变，避免"先删旧、后生成失败"导致上一版草稿永久丢失。
        var output = await _schedulingEngine.GenerateAsync(storeId, request.StartDate, request.EndDate, cancellationToken);

        var planName = request.PlanName?.Trim();
        if (string.IsNullOrWhiteSpace(planName))
        {
            planName = $"{request.StartDate:yyyy-MM-dd} 至 {request.EndDate:yyyy-MM-dd} 排班";
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 级联删除旧草稿及其关联数据（与新数据写入同一事务，失败整体回滚）
        if (existingPlan is not null)
        {
            await _dbContext.ShiftSwaps
                .Where(x => x.PlanId == existingPlan.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext.ScheduleResults
                .Where(x => x.PlanId == existingPlan.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext.ScheduleSummaries
                .Where(x => x.PlanId == existingPlan.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext.ScheduleIssues
                .Where(x => x.PlanId == existingPlan.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext.SchedulePlans
                .Where(x => x.Id == existingPlan.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var plan = new SchedulePlanEntity
        {
            StoreId = storeId,
            PlanName = planName,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = "DRAFT",
            CreatedBy = operatorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.SchedulePlans.Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var assignment in output.WorkstationAssignments)
        {
            _dbContext.ScheduleResults.Add(new ScheduleResultEntity
            {
                PlanId = plan.Id,
                StoreId = storeId,
                EmployeeId = assignment.EmployeeId,
                WorkDate = assignment.WorkDate,
                // 临时班次（需求缺口自动生成，模板 Id 为负数）不落模板外键
                ShiftTemplateId = assignment.ShiftTemplateId > 0 ? assignment.ShiftTemplateId : null,
                TimeSlot = assignment.TimeSlot,
                WorkstationId = assignment.WorkstationId,
                SkillScore = assignment.SkillScore,
                Status = "DRAFT",
                Version = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // 每员工每天一个班次、一次班中休息；按开始时间取最早一条（汇总表仅存单条休息）
        var breakByEmployeeDate = output.BreakAssignments
            .GroupBy(x => (x.EmployeeId, x.WorkDate))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.BreakStartTime).First());

        foreach (var summary in output.DaySummaries)
        {
            var entity = new ScheduleSummaryEntity
            {
                PlanId = plan.Id,
                StoreId = storeId,
                EmployeeId = summary.EmployeeId,
                WorkDate = summary.WorkDate,
                IsRestDay = summary.IsRestDay,
                ShiftTemplateId = summary.ShiftTemplateId > 0 ? summary.ShiftTemplateId : null,
                StartTime = summary.StartTime,
                EndTime = summary.EndTime,
                WorkHours = summary.WorkHours,
                CoveredWorkstations = summary.CoveredWorkstations,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // 班中休息（每次固定 30 分钟）：跨午夜槽结束时间按 24 小时制回绕
            if (breakByEmployeeDate.TryGetValue((summary.EmployeeId, summary.WorkDate), out var br))
            {
                entity.BreakStartTime = br.BreakStartTime;
                entity.BreakEndTime = TimeSpan.FromMinutes(((int)br.BreakStartTime.TotalMinutes + 30) % 1440);
                entity.BreakCoverEmployeeId = br.CoverEmployeeId;
                entity.BreakWorkstationId = br.WorkstationId;
            }

            _dbContext.ScheduleSummaries.Add(entity);
        }

        foreach (var issue in output.Issues)
        {
            _dbContext.ScheduleIssues.Add(new ScheduleIssueEntity
            {
                PlanId = plan.Id,
                StoreId = storeId,
                IssueType = issue.IssueType,
                Severity = issue.Severity,
                WorkDate = issue.WorkDate,
                TimeSlot = issue.TimeSlot,
                EmployeeId = issue.EmployeeId,
                WorkstationId = issue.WorkstationId,
                Description = issue.Description,
                Status = "OPEN",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // P1-7 修复：审计日志与业务数据在同一事务内原子提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "GENERATE_SCHEDULE",
            "SCHEDULE_PLAN",
            plan.Id,
            null,
            JsonSerializer.Serialize(new
            {
                plan.StartDate,
                plan.EndDate,
                RestDayCount = output.RestDays.Count,
                ShiftAssignmentCount = output.ShiftAssignments.Count,
                IssueCount = output.Issues.Count
            }),
            "一键生成排班",
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new GenerateScheduleResult(
            plan.Id,
            plan.PlanName,
            output.RestDays.Count,
            output.ShiftAssignments.Count,
            output.WorkstationAssignments.Count,
            output.DaySummaries.Count,
            output.Issues.Count,
            output.Issues.GroupBy(x => x.IssueType).Select(g => g.Key).ToList(),
            output.DemandCoverage.DemandMinHours,
            output.DemandCoverage.DemandIdealHours,
            output.DemandCoverage.CoveredHours,
            output.DemandCoverage.GapHours,
            output.DemandCoverage.CoveragePct,
            output.DemandCoverage.DemandShiftCount);
    }

    public async Task<PagedResult<SchedulePlanItem>> ListPlansAsync(
        int page,
        int pageSize,
        long storeId,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.SchedulePlans
            .AsNoTracking()
            .Where(x => x.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);

        var plans = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var planIds = plans.Select(x => x.Id).ToList();

        var employeeCounts = await _dbContext.ScheduleSummaries
            .AsNoTracking()
            .Where(x => planIds.Contains(x.PlanId))
            .GroupBy(x => x.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Select(s => s.EmployeeId).Distinct().Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, cancellationToken);

        var issueCounts = await _dbContext.ScheduleIssues
            .AsNoTracking()
            .Where(x => planIds.Contains(x.PlanId))
            .GroupBy(x => x.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, cancellationToken);

        var items = plans.Select(p => new SchedulePlanItem(
            p.Id,
            p.PlanName,
            p.StartDate,
            p.EndDate,
            p.Status,
            p.CreatedBy,
            p.PublishedAt,
            p.CreatedAt,
            employeeCounts.GetValueOrDefault(p.Id),
            issueCounts.GetValueOrDefault(p.Id))).ToList();

        return PagedResult<SchedulePlanItem>.Create(page, pageSize, total, items);
    }

    public async Task<IReadOnlyList<MonthViewItem>> GetMonthViewAsync(
        long planId,
        long storeId,
        CancellationToken cancellationToken)
    {
        await GetPlanAsync(planId, storeId, cancellationToken);

        var summaries = await _dbContext.ScheduleSummaries
            .AsNoTracking()
            .Where(x => x.PlanId == planId)
            .ToListAsync(cancellationToken);

        var shiftCodes = await _dbContext.ShiftTemplates
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var employeeById = (await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => new { x.Id, x.EmployeeNo, x.Name, x.Department, x.IsParttime })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id);

        return summaries
            .GroupBy(x => x.EmployeeId)
            // 3.14 修复：孤儿汇总（员工档案缺失）跳过而非抛异常 → 500
            .Where(g => employeeById.ContainsKey(g.Key))
            .Select(g =>
            {
                var employee = employeeById[g.Key];
                var days = g.OrderBy(x => x.WorkDate)
                    .Select(s => new MonthDayCell(
                        s.WorkDate,
                        s.IsRestDay,
                        s.ShiftTemplateId is null ? null : shiftCodes.GetValueOrDefault(s.ShiftTemplateId.Value),
                        s.WorkHours,
                        s.BreakStartTime,
                        s.BreakEndTime))
                    .ToList();

                return new MonthViewItem(employee.Id, employee.EmployeeNo, employee.Name, employee.Department, employee.IsParttime, days);
            })
            // 全职在前、兼职在后，同组内按工号排序
            .OrderBy(x => x.IsParttime)
            .ThenBy(x => x.EmployeeNo)
            .ToList();
    }

    public async Task<IReadOnlyList<WeekViewItem>> GetWeekViewAsync(
        long planId,
        long storeId,
        DateOnly? weekStart,
        CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(planId, storeId, cancellationToken);

        var start = weekStart ?? plan.StartDate;

        var summaries = await _dbContext.ScheduleSummaries
            .AsNoTracking()
            .Where(x => x.PlanId == planId && x.WorkDate >= start && x.WorkDate < start.AddDays(7))
            .ToListAsync(cancellationToken);

        var shiftCodes = await _dbContext.ShiftTemplates
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var employeeById = (await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => new { x.Id, x.EmployeeNo, x.Name, x.Department, x.IsParttime })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id);

        var coverIds = summaries
            .Where(x => x.BreakCoverEmployeeId is not null)
            .Select(x => x.BreakCoverEmployeeId!.Value)
            .Distinct()
            .ToList();
        var coverNames = coverIds.Count == 0
            ? new Dictionary<long, string>()
            : await _dbContext.Employees
                .AsNoTracking()
                .Where(x => coverIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return summaries
            .GroupBy(x => x.EmployeeId)
            // 3.14 修复：孤儿汇总跳过而非抛异常 → 500
            .Where(g => employeeById.ContainsKey(g.Key))
            .Select(g =>
            {
                var employee = employeeById[g.Key];
                var days = g.OrderBy(x => x.WorkDate)
                    .Select(s => new WeekDayShift(
                        s.WorkDate,
                        s.IsRestDay,
                        s.ShiftTemplateId is null ? null : shiftCodes.GetValueOrDefault(s.ShiftTemplateId.Value),
                        s.StartTime,
                        s.EndTime,
                        s.WorkHours,
                        s.CoveredWorkstations,
                        s.BreakStartTime,
                        s.BreakEndTime,
                        s.BreakCoverEmployeeId is null ? null : coverNames.GetValueOrDefault(s.BreakCoverEmployeeId.Value)))
                    .ToList();

                return new WeekViewItem(employee.Id, employee.EmployeeNo, employee.Name, employee.Department, employee.IsParttime, days);
            })
            // 全职在前、兼职在后，同组内按工号排序
            .OrderBy(x => x.IsParttime)
            .ThenBy(x => x.EmployeeNo)
            .ToList();
    }

    public async Task<IReadOnlyList<DailyViewItem>> GetDailyViewAsync(
        long planId,
        long storeId,
        DateOnly workDate,
        CancellationToken cancellationToken)
    {
        await GetPlanAsync(planId, storeId, cancellationToken);

        var results = await _dbContext.ScheduleResults
            .AsNoTracking()
            .Where(x => x.PlanId == planId && x.WorkDate == workDate)
            .ToListAsync(cancellationToken);

        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => new { x.Id, x.EmployeeNo, x.Name, x.PrimaryPosition, x.IsParttime })
            .ToListAsync(cancellationToken);

        var shiftCodes = await _dbContext.ShiftTemplates
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var workstationNames = await _dbContext.Workstations
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        // 当日班中休息与顶岗信息
        var breaksToday = await _dbContext.ScheduleSummaries
            .AsNoTracking()
            .Where(x => x.PlanId == planId && x.WorkDate == workDate && x.BreakStartTime != null)
            .Select(x => new { x.EmployeeId, x.BreakStartTime, x.BreakEndTime, x.BreakCoverEmployeeId })
            .ToListAsync(cancellationToken);

        var breakByEmployee = breaksToday
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var coverIds = breaksToday
            .Where(x => x.BreakCoverEmployeeId is not null)
            .Select(x => x.BreakCoverEmployeeId!.Value)
            .Distinct()
            .ToList();
        var coverNames = coverIds.Count == 0
            ? new Dictionary<long, string>()
            : await _dbContext.Employees
                .AsNoTracking()
                .Where(x => coverIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return results
            .OrderBy(x => x.TimeSlot)
            .ThenBy(x => x.EmployeeId)
            .Select(r =>
            {
                var employee = employees.FirstOrDefault(e => e.Id == r.EmployeeId);
                var brk = breakByEmployee.TryGetValue(r.EmployeeId, out var b) ? b : null;
                return new DailyViewItem(
                    r.WorkDate,
                    r.EmployeeId,
                    employee?.EmployeeNo ?? "--",
                    employee?.Name ?? "--",
                    employee?.PrimaryPosition,
                    employee?.IsParttime ?? 0,
                    r.ShiftTemplateId,
                    r.ShiftTemplateId is null ? null : shiftCodes.GetValueOrDefault(r.ShiftTemplateId.Value),
                    r.WorkstationId,
                    r.WorkstationId is null ? null : workstationNames.GetValueOrDefault(r.WorkstationId.Value),
                    r.TimeSlot,
                    r.SkillScore,
                    brk?.BreakStartTime,
                    brk?.BreakEndTime,
                    brk?.BreakCoverEmployeeId is null ? null : coverNames.GetValueOrDefault(brk.BreakCoverEmployeeId.Value));
            })
            .ToList();
    }

    public async Task<ScheduleSummaryDto> GetSummaryAsync(long planId, long storeId, CancellationToken cancellationToken)
    {
        await GetPlanAsync(planId, storeId, cancellationToken);

        var summaries = await _dbContext.ScheduleSummaries
            .AsNoTracking()
            .Where(x => x.PlanId == planId)
            .ToListAsync(cancellationToken);

        var issues = await _dbContext.ScheduleIssues
            .AsNoTracking()
            .Where(x => x.PlanId == planId)
            .ToListAsync(cancellationToken);

        return new ScheduleSummaryDto(
            planId,
            summaries.Select(x => x.EmployeeId).Distinct().Count(),
            summaries.Count(x => x.IsRestDay == 1),
            summaries.Count(x => x.IsRestDay == 0),
            summaries.Where(x => x.IsRestDay == 0).Sum(x => x.WorkHours),
            issues.Count(x => x.IssueType == "STAFFING_GAP"),
            issues.Count(x => x.Severity == "WARN"),
            issues.Count(x => x.Severity == "ERROR"));
    }

    public async Task AdjustAsync(
        long planId,
        AdjustScheduleRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(planId, storeId, cancellationToken);

        if (plan.Status == "PUBLISHED")
        {
            throw new BusinessException("已发布的排班不能直接调整，请作废后重新生成", "SCHEDULE_PUBLISHED");
        }

        // 修复：多槽位班次必须整体调整——原来只改第一条明细，其余槽位残留旧班次/旧工作站；
        // 删除+重建使用 ExecuteDeleteAsync（立即落库），与后续 SaveChanges 之间包事务保证原子性。
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            if (item.ShiftTemplateId is null && item.WorkstationId is null)
            {
                throw new BusinessException("调整项必须指定班次或工作站", "INVALID_ADJUST");
            }

            var dayRows = await _dbContext.ScheduleResults
                .AsNoTracking()
                .Where(x =>
                    x.PlanId == planId &&
                    x.EmployeeId == item.EmployeeId &&
                    x.WorkDate == item.WorkDate)
                .OrderBy(x => x.TimeSlot)
                .ToListAsync(cancellationToken);

            if (dayRows.Count == 0)
            {
                throw new BusinessException($"未找到员工 {item.EmployeeId} 在 {item.WorkDate:yyyy-MM-dd} 的排班记录", "ADJUST_NOT_FOUND");
            }

            ShiftTemplateEntity? newShift = null;
            if (item.ShiftTemplateId is not null)
            {
                newShift = await _dbContext.ShiftTemplates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == item.ShiftTemplateId && x.StoreId == storeId && x.Status == 1, cancellationToken)
                    ?? throw new BusinessException("调整的班次不存在或已停用", "INVALID_SHIFT");
            }

            // 目标工作站：显式指定则校验技能；仅改班次时取新班次覆盖站中技能分最高者
            long? targetWs = null;
            var skillScore = 0;
            if (item.WorkstationId is not null)
            {
                skillScore = await _dbContext.EmployeeSkills
                    .Where(x =>
                        x.EmployeeId == item.EmployeeId &&
                        x.WorkstationId == item.WorkstationId &&
                        x.SkillScore > 0 &&
                        x.Status == 1)
                    .Select(x => (int?)x.SkillScore)
                    .FirstOrDefaultAsync(cancellationToken) ?? 0;

                if (skillScore == 0)
                {
                    throw new BusinessException("员工不具备目标工作站技能，无法调整", "INVALID_ADJUST");
                }

                targetWs = item.WorkstationId;
            }
            else if (newShift is not null)
            {
                var shiftWsIds = await _dbContext.ShiftWorkstations
                    .Where(x => x.ShiftTemplateId == newShift.Id)
                    .Select(x => x.WorkstationId)
                    .ToListAsync(cancellationToken);

                var skills = await _dbContext.EmployeeSkills
                    .Where(x => x.EmployeeId == item.EmployeeId && x.Status == 1 && x.SkillScore > 0)
                    .ToDictionaryAsync(x => x.WorkstationId, x => x.SkillScore, cancellationToken);

                var bestWs = shiftWsIds
                    .Where(skills.ContainsKey)
                    .OrderByDescending(ws => skills[ws])
                    .FirstOrDefault();

                if (bestWs == 0)
                {
                    throw new BusinessException("员工在新班次覆盖的工作站上无技能，无法调整", "INVALID_ADJUST");
                }

                targetWs = bestWs;
                skillScore = skills[bestWs];
            }

            if (newShift is not null)
            {
                if (targetWs is not null)
                {
                    var shiftWsIds = await _dbContext.ShiftWorkstations
                        .Where(x => x.ShiftTemplateId == newShift.Id)
                        .Select(x => x.WorkstationId)
                        .ToListAsync(cancellationToken);
                    if (!shiftWsIds.Contains(targetWs.Value))
                    {
                        throw new BusinessException("所选工作站不属于新班次", "INVALID_ADJUST");
                    }
                }

                // 重建当天全部明细：按新班次的所有时段（含跨午夜回绕）写入
                await _dbContext.ScheduleResults
                    .Where(x => x.PlanId == planId && x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate)
                    .ExecuteDeleteAsync(cancellationToken);

                var slots = SchedulingTimeHelper.GetShiftSlots(newShift.StartTime, newShift.EndTime, newShift.IsCrossDay);
                foreach (var slot in slots)
                {
                    _dbContext.ScheduleResults.Add(new ScheduleResultEntity
                    {
                        PlanId = planId,
                        StoreId = storeId,
                        EmployeeId = item.EmployeeId,
                        WorkDate = item.WorkDate,
                        ShiftTemplateId = newShift.Id,
                        TimeSlot = slot,
                        WorkstationId = targetWs,
                        SkillScore = skillScore,
                        Status = plan.Status,
                        Version = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }
            else
            {
                // 仅改工作站：更新当天【全部】槽位明细（原来只改第一条）
                await _dbContext.ScheduleResults
                    .Where(x => x.PlanId == planId && x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.WorkstationId, targetWs!.Value)
                        .SetProperty(x => x.SkillScore, skillScore)
                        .SetProperty(x => x.UpdatedAt, DateTime.UtcNow)
                        .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            }

            // 同步日汇总（班次变化影响工时和覆盖范围）
            var summary = await _dbContext.ScheduleSummaries
                .FirstOrDefaultAsync(x =>
                    x.PlanId == planId &&
                    x.EmployeeId == item.EmployeeId &&
                    x.WorkDate == item.WorkDate,
                    cancellationToken);

            if (summary is not null)
            {
                if (newShift is not null)
                {
                    summary.ShiftTemplateId = newShift.Id;
                    summary.StartTime = newShift.StartTime;
                    summary.EndTime = newShift.EndTime;
                    summary.WorkHours = SchedulingTimeHelper.GetShiftHours(newShift.StartTime, newShift.EndTime, newShift.IsCrossDay);
                }

                if (targetWs is not null)
                {
                    summary.CoveredWorkstations = targetWs.Value.ToString();
                }

                summary.UpdatedAt = DateTime.UtcNow;
            }

            // 该员工当天班次/工作站已变化：清理当天其他员工休息记录中对他的顶岗引用，避免残留失效顶岗
            await _dbContext.ScheduleSummaries
                .Where(x => x.PlanId == planId && x.WorkDate == item.WorkDate && x.BreakCoverEmployeeId == item.EmployeeId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.BreakCoverEmployeeId, (long?)null)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken);
        }

        // 修复：审计与业务数据在同一事务内提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "ADJUST_SCHEDULE",
            "SCHEDULE_PLAN",
            planId,
            null,
            JsonSerializer.Serialize(request.Items),
            $"手动调整排班 {plan.PlanName}，共 {request.Items.Count} 项",
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 设置员工某天的休息/上班状态（日明细点色块调整）：
    /// 设为休息 → 删除当天全部明细并把日汇总改为休息；
    /// 设为上班 → 按所选班次重建当天明细（工作站默认取员工技能分最高的），并更新日汇总。
    /// 仅允许调整草稿计划。
    /// </summary>
    public async Task SetDayStatusAsync(
        long planId,
        SetDayStatusRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(planId, storeId, cancellationToken);

        if (plan.Status == "PUBLISHED")
        {
            throw new BusinessException("已发布的排班不能直接调整，请作废后重新生成", "SCHEDULE_PUBLISHED");
        }

        // 2.1 修复：ExecuteDeleteAsync 立即落库、新增明细在最后 SaveChanges 才提交，
        // 若中途失败会留下「明细已删、汇总未改」的损坏数据；整体放入事务保证原子性。
        // （await using 保证异常传播时事务自动回滚）
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            if (item.IsRestDay == 1)
            {
                // 设为休息：删除当天全部明细，日汇总改为休息
                await _dbContext.ScheduleResults
                    .Where(x => x.PlanId == planId && x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate)
                    .ExecuteDeleteAsync(cancellationToken);

                var restSummary = await _dbContext.ScheduleSummaries
                    .FirstOrDefaultAsync(x => x.PlanId == planId && x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate, cancellationToken);

                if (restSummary is null)
                {
                    _dbContext.ScheduleSummaries.Add(new ScheduleSummaryEntity
                    {
                        PlanId = planId,
                        StoreId = storeId,
                        EmployeeId = item.EmployeeId,
                        WorkDate = item.WorkDate,
                        IsRestDay = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    restSummary.IsRestDay = 1;
                    restSummary.ShiftTemplateId = null;
                    restSummary.StartTime = null;
                    restSummary.EndTime = null;
                    restSummary.WorkHours = 0;
                    restSummary.CoveredWorkstations = null;
                    restSummary.BreakStartTime = null;
                    restSummary.BreakEndTime = null;
                    restSummary.BreakCoverEmployeeId = null;
                    restSummary.BreakWorkstationId = null;
                    restSummary.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                // 设为上班：必须指定班次
                if (item.ShiftTemplateId is null)
                {
                    throw new BusinessException("设为上班必须选择班次", "INVALID_ADJUST");
                }

                var shift = await _dbContext.ShiftTemplates
                    .FirstOrDefaultAsync(x => x.Id == item.ShiftTemplateId && x.StoreId == storeId && x.Status == 1, cancellationToken)
                    ?? throw new BusinessException("班次不存在或已停用", "INVALID_SHIFT");

                var shiftWsIds = await _dbContext.ShiftWorkstations
                    .Where(x => x.ShiftTemplateId == shift.Id)
                    .Select(x => x.WorkstationId)
                    .ToListAsync(cancellationToken);

                if (shiftWsIds.Count == 0)
                {
                    throw new BusinessException("该班次未关联工作站，无法安排", "INVALID_SHIFT");
                }

                var skills = await _dbContext.EmployeeSkills
                    .Where(x => x.EmployeeId == item.EmployeeId && x.Status == 1 && x.SkillScore > 0)
                    .ToDictionaryAsync(x => x.WorkstationId, x => x.SkillScore, cancellationToken);

                long? targetWs;
                if (item.WorkstationId is not null)
                {
                    if (!shiftWsIds.Contains(item.WorkstationId.Value))
                    {
                        throw new BusinessException("所选工作站不属于该班次", "INVALID_ADJUST");
                    }

                    if (!skills.ContainsKey(item.WorkstationId.Value))
                    {
                        throw new BusinessException("员工不具备所选工作站技能，无法安排", "INVALID_ADJUST");
                    }

                    targetWs = item.WorkstationId.Value;
                }
                else
                {
                    // 默认取该班次覆盖工作站中员工技能分最高的
                    targetWs = shiftWsIds
                        .Where(skills.ContainsKey)
                        .OrderByDescending(ws => skills[ws])
                        .FirstOrDefault();
                }

                if (targetWs is null)
                {
                    throw new BusinessException("员工在该班次覆盖的工作站上无技能，无法安排", "INVALID_ADJUST");
                }

                var skillScore = skills.GetValueOrDefault(targetWs.Value);
                var shiftHours = SchedulingTimeHelper.GetShiftHours(shift.StartTime, shift.EndTime, shift.IsCrossDay);
                var slots = SchedulingTimeHelper.GetShiftSlots(shift.StartTime, shift.EndTime, shift.IsCrossDay);

                // 幂等：先删除该员工该日全部明细，再按新班次重建
                await _dbContext.ScheduleResults
                    .Where(x => x.PlanId == planId && x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate)
                    .ExecuteDeleteAsync(cancellationToken);

                foreach (var slot in slots)
                {
                    _dbContext.ScheduleResults.Add(new ScheduleResultEntity
                    {
                        PlanId = planId,
                        StoreId = storeId,
                        EmployeeId = item.EmployeeId,
                        WorkDate = item.WorkDate,
                        ShiftTemplateId = shift.Id,
                        TimeSlot = slot,
                        WorkstationId = targetWs,
                        SkillScore = skillScore,
                        Status = plan.Status,
                        Version = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                var workSummary = await _dbContext.ScheduleSummaries
                    .FirstOrDefaultAsync(x => x.PlanId == planId && x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate, cancellationToken);

                if (workSummary is null)
                {
                    _dbContext.ScheduleSummaries.Add(new ScheduleSummaryEntity
                    {
                        PlanId = planId,
                        StoreId = storeId,
                        EmployeeId = item.EmployeeId,
                        WorkDate = item.WorkDate,
                        IsRestDay = 0,
                        ShiftTemplateId = shift.Id,
                        StartTime = shift.StartTime,
                        EndTime = shift.EndTime,
                        WorkHours = shiftHours,
                        CoveredWorkstations = targetWs.Value.ToString(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    workSummary.IsRestDay = 0;
                    workSummary.ShiftTemplateId = shift.Id;
                    workSummary.StartTime = shift.StartTime;
                    workSummary.EndTime = shift.EndTime;
                    workSummary.WorkHours = shiftHours;
                    workSummary.CoveredWorkstations = targetWs.Value.ToString();
                    workSummary.BreakStartTime = null;
                    workSummary.BreakEndTime = null;
                    workSummary.BreakCoverEmployeeId = null;
                    workSummary.BreakWorkstationId = null;
                    workSummary.UpdatedAt = DateTime.UtcNow;
                }
            }

            // 该员工当天状态已变化：清理当天其他员工休息记录中对他的顶岗引用，避免残留失效顶岗
            await _dbContext.ScheduleSummaries
                .Where(x => x.PlanId == planId && x.WorkDate == item.WorkDate && x.BreakCoverEmployeeId == item.EmployeeId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.BreakCoverEmployeeId, (long?)null)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken);
        }

        // 审计与业务数据在同一事务内原子提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "SET_DAY_STATUS",
            "SCHEDULE_PLAN",
            planId,
            null,
            JsonSerializer.Serialize(request.Items),
            $"手动设置员工休息/上班状态 {plan.PlanName}，共 {request.Items.Count} 项",
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 调整员工某天某个半小时时段的状态（甘特图/日明细点色块）：
    /// IsRest=1 → 该半小时改为休息（记为班中休息：BreakStartTime=该时段，30 分钟）；
    /// IsRest=0 → 该半小时恢复上班（清除休息标记）。
    /// 仅允许调整草稿计划。
    /// </summary>
    public async Task SetSlotStatusAsync(
        long planId,
        SetSlotStatusRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(planId, storeId, cancellationToken);

        if (plan.Status == "PUBLISHED")
        {
            throw new BusinessException("已发布的排班不能直接调整，请作废后重新生成", "SCHEDULE_PUBLISHED");
        }

        foreach (var item in request.Items)
        {
            // 该员工该时段必须有排班明细（休息标记基于班次存在）
            var slotResult = await _dbContext.ScheduleResults
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.PlanId == planId && x.EmployeeId == item.EmployeeId &&
                    x.WorkDate == item.WorkDate && x.TimeSlot == item.TimeSlot,
                    cancellationToken)
                ?? throw new BusinessException("该员工在该时段没有排班记录，无法调整", "SLOT_NOT_FOUND");

            var summary = await _dbContext.ScheduleSummaries
                .FirstOrDefaultAsync(x =>
                    x.PlanId == planId && x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate,
                    cancellationToken)
                ?? throw new BusinessException("该员工当天没有排班汇总记录，无法调整", "SLOT_NOT_FOUND");

            if (item.IsRest == 1)
            {
                // 该半小时改为休息：休息时间段固定 30 分钟，从该时段开始（跨午夜按 24 小时回绕）
                summary.BreakStartTime = item.TimeSlot;
                summary.BreakEndTime = TimeSpan.FromMinutes(((int)item.TimeSlot.TotalMinutes + 30) % 1440);
                summary.BreakCoverEmployeeId = null;
                summary.BreakWorkstationId = slotResult.WorkstationId;
            }
            else
            {
                // 该半小时恢复上班：清除休息标记
                summary.BreakStartTime = null;
                summary.BreakEndTime = null;
                summary.BreakCoverEmployeeId = null;
                summary.BreakWorkstationId = null;
            }

            summary.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId,
            operatorUserId,
            operatorName,
            "SET_SLOT_STATUS",
            "SCHEDULE_PLAN",
            planId,
            null,
            JsonSerializer.Serialize(request.Items),
            $"手动调整时段休息/上班状态 {plan.PlanName}，共 {request.Items.Count} 项",
            cancellationToken);
    }

    public async Task<MoveScheduleSegmentResult> MoveSegmentAsync(
        long planId,
        MoveScheduleSegmentRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(planId, storeId, cancellationToken);

        if (plan.Status == "PUBLISHED")
        {
            throw new BusinessException("已发布的排班不能直接调整，请作废后重新生成", "SCHEDULE_PUBLISHED");
        }

        if (!TimeSpan.TryParse(request.FromTimeSlot, System.Globalization.CultureInfo.InvariantCulture, out var fromSlot) ||
            !TimeSpan.TryParse(request.ToTimeSlot, System.Globalization.CultureInfo.InvariantCulture, out var toSlot))
        {
            throw new BusinessException("时段格式不正确（需 HH:mm）", "INVALID_TIME_SLOT");
        }

        // 目标工作站技能校验
        var targetSkill = await _dbContext.EmployeeSkills
            .FirstOrDefaultAsync(x =>
                x.EmployeeId == request.EmployeeId &&
                x.WorkstationId == request.ToWorkstationId &&
                x.SkillScore > 0 &&
                x.Status == 1, cancellationToken)
            ?? throw new BusinessException("员工不具备目标工作站技能，无法移动", "INVALID_ADJUST");

        var delta = toSlot - fromSlot;
        if (delta.TotalMinutes % 30 != 0 || delta == TimeSpan.Zero && request.FromWorkstationId == request.ToWorkstationId)
        {
            throw new BusinessException("目标位置与原位置相同或未按 30 分钟对齐", "INVALID_MOVE");
        }

        var deltaMinutes = (int)delta.TotalMinutes;

        // 该员工当天在该工作站的连续段（含起始时段）
        var dayRows = await _dbContext.ScheduleResults
            .Where(x => x.PlanId == planId && x.EmployeeId == request.EmployeeId && x.WorkDate == request.WorkDate)
            .OrderBy(x => x.TimeSlot)
            .ToListAsync(cancellationToken);

        var segmentRows = new List<ScheduleResultEntity>();
        var slotsInSegment = new HashSet<TimeSpan>();

        // 找到起始行：只移动该半小时（除非前端框选多格另传）
        var startRow = dayRows.FirstOrDefault(x =>
            x.WorkstationId == request.FromWorkstationId && x.TimeSlot == fromSlot);
        if (startRow is null)
        {
            throw new BusinessException("未找到该员工在该工作站该时段的排班记录", "MOVE_NOT_FOUND");
        }

        segmentRows.Add(startRow);
        slotsInSegment.Add(startRow.TimeSlot);

        // 计算新时段并校验（冲突检查排除移动段自身的行）
        var movingIds = segmentRows.Select(x => x.Id).ToHashSet();
        foreach (var row in segmentRows)
        {
            var newSlot = row.TimeSlot + delta;
            if (newSlot < TimeSpan.Zero || newSlot >= TimeSpan.FromHours(24))
            {
                throw new BusinessException("移动后时段会跨出当天（跨午夜平移暂不支持）", "INVALID_MOVE");
            }

            // 与该员工当天其他段重叠检查
            var collision = dayRows.Any(x =>
                !movingIds.Contains(x.Id) && x.TimeSlot == newSlot);
            if (collision)
            {
                throw new BusinessException("目标时段与该员工当天已有安排重叠", "MOVE_CONFLICT");
            }
        }

        // 修复：移动 + 顶岗引用清理 + 审计在同一事务内原子提交
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 应用移动
        foreach (var row in segmentRows)
        {
            row.TimeSlot = row.TimeSlot + delta;
            row.WorkstationId = request.ToWorkstationId;
            row.SkillScore = targetSkill.SkillScore;
            row.UpdatedAt = DateTime.UtcNow;
            row.Version++;
        }

        // 同步日汇总（起止时间与工时按当天实际时段重算）
        var summary = await _dbContext.ScheduleSummaries
            .FirstOrDefaultAsync(x =>
                x.PlanId == planId &&
                x.EmployeeId == request.EmployeeId &&
                x.WorkDate == request.WorkDate, cancellationToken);

        if (summary is not null)
        {
            var allSlots = dayRows
                .Where(x => !slotsInSegment.Contains(x.TimeSlot))
                .Select(x => x.TimeSlot)
                .Concat(segmentRows.Select(x => x.TimeSlot + delta))
                .OrderBy(x => x)
                .ToList();

            if (allSlots.Count > 0)
            {
                summary.StartTime = allSlots.Min();
                summary.EndTime = allSlots.Max() + TimeSpan.FromMinutes(30);
                summary.WorkHours = allSlots.Count * 0.5m;
                summary.UpdatedAt = DateTime.UtcNow;
            }
        }

        // 移动后该员工在原工作站该时段的覆盖失效：清理当天其他员工休息记录中对他的顶岗引用
        await _dbContext.ScheduleSummaries
            .Where(x => x.PlanId == planId && x.WorkDate == request.WorkDate && x.BreakCoverEmployeeId == request.EmployeeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.BreakCoverEmployeeId, (long?)null)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken);

        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "MOVE_SCHEDULE_SEGMENT",
            "SCHEDULE_RESULT",
            planId,
            $"{request.EmployeeId}|{request.WorkDate:yyyy-MM-dd}|{request.FromWorkstationId}|{request.FromTimeSlot}",
            $"{request.ToWorkstationId}|{request.ToTimeSlot}",
            $"拖动移动员工 {request.EmployeeId} 的半小时（平移 {deltaMinutes} 分钟，工作站 {request.FromWorkstationId}→{request.ToWorkstationId}）",
            DateTime.UtcNow);

        // 审计实体须在 SaveChanges 之前加入，否则不会被持久化
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new MoveScheduleSegmentResult(segmentRows.Count, request.FromTimeSlot, request.ToTimeSlot, request.ToWorkstationId);
    }

    public async Task PublishAsync(
        long planId,
        long storeId,
        long operatorUserId,
        string operatorName,
        bool force,
        CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SchedulePlans
            .FirstOrDefaultAsync(x => x.Id == planId && x.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("排班计划不存在");

        if (plan.Status == "PUBLISHED")
        {
            throw new BusinessException("排班已发布", "ALREADY_PUBLISHED");
        }

        var errors = await _dbContext.ScheduleIssues
            .AnyAsync(x => x.PlanId == planId && x.Severity == "ERROR", cancellationToken);

        if (errors && !force)
        {
            // 前端先检查并弹"确认继续发布"；force=true 表示用户已确认
            throw new BusinessException("排班存在严重违规（ERROR），请先处理后再发布", "HAS_ERROR_ISSUES");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        plan.Status = "PUBLISHED";
        plan.PublishedAt = DateTime.UtcNow;
        plan.UpdatedAt = DateTime.UtcNow;

        await _dbContext.ScheduleResults
            .Where(x => x.PlanId == planId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "PUBLISHED"), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 3.8 修复：PlanName 为管理员输入，入库前 HtmlEncode（与员工通知一致）
        var encodedPlanName = WebUtility.HtmlEncode(plan.PlanName);
        var notify = new NotificationEntity
        {
            StoreId = storeId,
            ReceiverUserId = operatorUserId,
            NotificationType = "SCHEDULE_PUBLISHED",
            Title = $"排班已发布：{encodedPlanName}",
            Content = $"排班计划 {encodedPlanName}（{plan.StartDate:yyyy-MM-dd} 至 {plan.EndDate:yyyy-MM-dd}）已正式发布。",
            IsRead = 0,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Notifications.Add(notify);

        // 3.9 修复：员工班表发布通知纳入同一事务（原先在端点层事务外发送，
        // 通知保存失败会返回 500 但排班已发布）
        var employeeIds = await _dbContext.ScheduleSummaries.AsNoTracking()
            .Where(x => x.PlanId == planId)
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var empId in employeeIds)
        {
            _dbContext.Notifications.Add(new NotificationEntity
            {
                StoreId = storeId,
                ReceiverEmployeeId = empId,
                NotificationType = "SCHEDULE_PUBLISHED",
                Title = "排班已发布",
                Content = $"排班计划「{encodedPlanName}」已发布，请查看您的班表",
                IsRead = 0,
                CreatedAt = DateTime.UtcNow
            });
        }

        // P1-7 修复：审计日志与业务数据在同一事务内原子提交
        _auditLogService.AddAuditEntity(
            _dbContext,
            storeId,
            operatorUserId,
            operatorName,
            "PUBLISH_SCHEDULE",
            "SCHEDULE_PLAN",
            planId,
            null,
            $"{plan.PlanName} 已发布",
            "发布排班",
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SchedulePlanEntity> GetPlanAsync(long planId, long storeId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SchedulePlans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == planId && x.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("排班计划不存在");

        return plan;
    }
}
