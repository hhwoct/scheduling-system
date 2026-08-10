namespace ShiftScheduling.Api.Infrastructure.Persistence.Entities;

public sealed class UserEntity
{
    public long Id { get; set; }

    public long? StoreId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
