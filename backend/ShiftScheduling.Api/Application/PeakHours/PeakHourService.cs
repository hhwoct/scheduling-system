using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Application.Common;
using ShiftScheduling.Api.Infrastructure.Audit;
using ShiftScheduling.Api.Infrastructure.Persistence;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Application.PeakHours;

public sealed class PeakHourService : IPeakHourService
{
    private const int MaxPeakHours = 10;

    private readonly ShiftSchedulingDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public PeakHourService(ShiftSchedulingDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<IReadOnlyList<PeakHourItem>> ListAsync(long storeId, CancellationToken cancellationToken)
    {
        return await _dbContext.PeakRestrictedHours
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.StartTime)
            .Select(x => new PeakHourItem(x.Id, x.StartTime, x.EndTime, x.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<PeakHourItem> CreateAsync(
        PeakHourUpsertRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var (start, end) = ParseAndValidate(request);

        var activeCount = await _dbContext.PeakRestrictedHours
            .CountAsync(x => x.StoreId == storeId && x.Status == 1, cancellationToken);
        if (activeCount >= MaxPeakHours)
        {
            throw new BusinessException($"高峰时段最多配置 {MaxPeakHours} 条", "TOO_MANY_PEAK_HOURS");
        }

        var overlap = await _dbContext.PeakRestrictedHours
            .AnyAsync(x => x.StoreId == storeId && x.Status == 1 &&
                           x.StartTime < end && start < x.EndTime, cancellationToken);
        if (overlap)
        {
            throw new BusinessException("高峰时段与已有配置重叠", "PEAK_HOUR_OVERLAP");
        }

        var entity = new PeakRestrictedHourEntity
        {
            StoreId = storeId,
            StartTime = start,
            EndTime = end,
            Status = request.Status == 0 ? 0 : 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.PeakRestrictedHours.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId, operatorUserId, operatorName,
            "CREATE_PEAK_HOUR", "PEAK_RESTRICTED_HOUR", entity.Id,
            null, FmtRange(start, end),
            $"新增高峰禁休时段 {FmtRange(start, end)}",
            cancellationToken);

        return new PeakHourItem(entity.Id, entity.StartTime, entity.EndTime, entity.Status);
    }

    public async Task<PeakHourItem> UpdateAsync(
        long id,
        PeakHourUpsertRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var (start, end) = ParseAndValidate(request);

        var entity = await _dbContext.PeakRestrictedHours
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("高峰时段不存在");

        var overlap = await _dbContext.PeakRestrictedHours
            .AnyAsync(x => x.StoreId == storeId && x.Status == 1 && x.Id != id &&
                           x.StartTime < end && start < x.EndTime, cancellationToken);
        if (overlap)
        {
            throw new BusinessException("高峰时段与已有配置重叠", "PEAK_HOUR_OVERLAP");
        }

        var before = FmtRange(entity.StartTime, entity.EndTime);
        entity.StartTime = start;
        entity.EndTime = end;
        entity.Status = request.Status == 0 ? 0 : 1;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId, operatorUserId, operatorName,
            "UPDATE_PEAK_HOUR", "PEAK_RESTRICTED_HOUR", entity.Id,
            before, FmtRange(start, end),
            "修改高峰禁休时段",
            cancellationToken);

        return new PeakHourItem(entity.Id, entity.StartTime, entity.EndTime, entity.Status);
    }

    public async Task DeleteAsync(
        long id,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.PeakRestrictedHours
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("高峰时段不存在");

        var before = FmtRange(entity.StartTime, entity.EndTime);
        _dbContext.PeakRestrictedHours.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.WriteAsync(
            storeId, operatorUserId, operatorName,
            "DELETE_PEAK_HOUR", "PEAK_RESTRICTED_HOUR", id,
            before, null,
            "删除高峰禁休时段",
            cancellationToken);
    }

    private static (TimeSpan Start, TimeSpan End) ParseAndValidate(PeakHourUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StartTime) ||
            !TimeSpan.TryParse(request.StartTime, System.Globalization.CultureInfo.InvariantCulture, out var start) ||
            !TimeSpan.TryParse(request.EndTime, System.Globalization.CultureInfo.InvariantCulture, out var end))
        {
            throw new BusinessException("高峰时段格式不正确，请使用 HH:mm", "INVALID_PEAK_HOUR");
        }

        if (start < TimeSpan.Zero || end > TimeSpan.FromHours(24) || start >= end)
        {
            throw new BusinessException("开始时间必须早于结束时间，且在 00:00-24:00 之间", "INVALID_PEAK_HOUR");
        }

        // 与算法时段粒度一致：30 分钟对齐
        if (start.Minutes % 30 != 0 || end.Minutes % 30 != 0)
        {
            throw new BusinessException("高峰时段必须按 30 分钟对齐", "INVALID_PEAK_HOUR");
        }

        return (start, end);
    }

    private static string FmtRange(TimeSpan start, TimeSpan end)
        => $"{(int)start.TotalHours:D2}:{start.Minutes:D2}-{(int)end.TotalHours:D2}:{end.Minutes:D2}";
}
