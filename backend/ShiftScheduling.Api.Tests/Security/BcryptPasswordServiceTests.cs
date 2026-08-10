using ShiftScheduling.Api.Infrastructure;

namespace ShiftScheduling.Api.Tests.Security;

public sealed class BcryptPasswordServiceTests
{
    private readonly BcryptPasswordService _service = new();

    [Fact]
    public void Verify_WhenPasswordMatches_ReturnsTrue()
    {
        var hash = _service.Hash("admin123");

        Assert.True(_service.Verify("admin123", hash));
    }

    [Fact]
    public void Verify_WhenOnlyPasswordPrefixIsProvided_ReturnsFalse()
    {
        var hash = _service.Hash("admin123");

        Assert.False(_service.Verify("admin", hash));
    }

    [Fact]
    public void Verify_WhenLegacyPlaintextPlaceholderIsProvided_ReturnsFalse()
    {
        Assert.False(_service.Verify("admin123", "admin123_dev_plaintext_replace_later"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-bcrypt-hash")]
    public void Verify_WhenHashIsInvalid_ReturnsFalse(string invalidHash)
    {
        Assert.False(_service.Verify("admin123", invalidHash));
    }
}