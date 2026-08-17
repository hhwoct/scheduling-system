namespace ShiftScheduling.Api.Application.PeakHours;

public interface IPeakHourService
{
    Task<IReadOnlyList<PeakHourItem>> ListAsync(long storeId, CancellationToken cancellationToken);

    Task<PeakHourItem> CreateAsync(PeakHourUpsertRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    Task<PeakHourItem> UpdateAsync(long id, PeakHourUpsertRequest request, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);

    Task DeleteAsync(long id, long storeId, long operatorUserId, string operatorName, CancellationToken cancellationToken);
}
