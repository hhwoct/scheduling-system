using ShiftScheduling.Api.Application.Security;

namespace ShiftScheduling.Api.Tests.TestInfra;

public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }

    public long? UserId { get; set; }

    public long? StoreId { get; set; }

    public string? Username { get; set; }

    public string? Nickname { get; set; }

    public string? Role { get; set; }
}
