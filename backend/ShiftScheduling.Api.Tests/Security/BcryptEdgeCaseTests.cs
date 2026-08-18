using ShiftScheduling.Api.Infrastructure;

namespace ShiftScheduling.Api.Tests.Security;

/// <summary>bcrypt 边界行为测试：盐唯一性与 72 字节截断。</summary>
public sealed class BcryptEdgeCaseTests
{
    private readonly BcryptPasswordService _service = new();

    [Fact]
    public void Hash_GeneratesUniqueSaltPerCall()
    {
        // 相同密码两次哈希结果必须不同（盐唯一），否则撞库/预计算攻击风险
        var hash1 = _service.Hash("admin123");
        var hash2 = _service.Hash("admin123");

        Assert.NotEqual(hash1, hash2);
        Assert.True(_service.Verify("admin123", hash1));
        Assert.True(_service.Verify("admin123", hash2));
    }

    [Fact]
    public void Verify_Bcrypt72ByteLimit_OnlyFirst72BytesMatter()
    {
        // bcrypt 只取前 72 字节：前 72 字节相同、尾缀不同的两个密码会相互验证通过。
        // 若日后改为不截断的实现（如 SHA-512 变体），本测试会提醒行为变化。
        var prefix = new string('a', 72);
        var hash = _service.Hash(prefix + "tail-one");

        Assert.True(_service.Verify(prefix + "tail-two", hash));
    }
}
