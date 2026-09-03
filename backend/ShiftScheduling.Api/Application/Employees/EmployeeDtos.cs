namespace ShiftScheduling.Api.Application.Employees;

public sealed record EmployeeListItem(
    long Id,
    string EmployeeNo,
    string Name,
    string? Phone,
    string Department,
    DateTime? HireDate,
    string? PrimaryPosition,
    decimal MaxWeeklyHours,
    int WeeklyHoursFollowDefault,
    int IsParttime,
    int Status,
    long StoreId,
    string? StoreName);

public sealed record EmployeeDetail(
    long Id,
    string EmployeeNo,
    string Name,
    string? Phone,
    string Department,
    DateTime? HireDate,
    string? PrimaryPosition,
    decimal MaxWeeklyHours,
    int WeeklyHoursFollowDefault,
    int IsParttime,
    int Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record EmployeeUpsertRequest(
    string EmployeeNo,
    string Name,
    string? Phone,
    string Department,
    DateTime? HireDate,
    string? PrimaryPosition,
    decimal MaxWeeklyHours,
    int WeeklyHoursFollowDefault = 1,
    // 超管新增员工时指定归属门店;非超管传入会被忽略(以后端登录态为准)
    long? StoreId = null);

public sealed record EmployeeQueryRequest(
    int Page = 1,
    int PageSize = 20,
    string? Name = null,
    string? EmployeeNo = null,
    string? Department = null,
    int? Status = null,
    long? StoreId = null);
