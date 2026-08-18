namespace ShiftScheduling.Api.Application.StaffingRequirements;

/// <summary>人数需求配置：平日/周末/节假日 × 工作站 × 30 分钟时段。</summary>
public interface IStaffingRequirementService
{
    /// <summary>查询人数需求；dayType 为空时返回全部日期类型。</summary>
    Task<IReadOnlyList<StaffingRequirementItem>> ListAsync(
        long storeId,
        string? dayType,
        CancellationToken cancellationToken);

    /// <summary>
    /// 批量保存某个日期类型的全矩阵人数需求（整店工作站 × 48 个半小时时段）。
    /// 请求中未提交的工作站时段会被置为 0。
    /// </summary>
    Task<StaffingRequirementSaveResult> SaveAsync(
        StaffingRequirementSaveRequest request,
        long storeId,
        long operatorUserId,
        string operatorName,
        CancellationToken cancellationToken);
}
