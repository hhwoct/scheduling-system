namespace ShiftScheduling.Api.Application.Common;

public sealed record PagedResult<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items)
{
    public static PagedResult<T> Create(int page, int pageSize, int total, IReadOnlyList<T> items)
        => new(page, pageSize, total, items);
}
