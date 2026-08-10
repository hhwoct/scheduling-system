namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class NotificationEntity
{
    public long Id { get; set; }

    public long StoreId { get; set; }

    public long? ReceiverUserId { get; set; }

    public long? ReceiverEmployeeId { get; set; }

    public string NotificationType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }
}
