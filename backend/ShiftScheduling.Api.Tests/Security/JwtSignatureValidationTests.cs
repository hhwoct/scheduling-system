using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShiftScheduling.Api.Application.Auth;
using ShiftScheduling.Api.Application.Security;
using ShiftScheduling.Api.Infrastructure;

namespace ShiftScheduling.Api.Tests.Security;

/// <summary>JWT 的密码学验证与防篡改测试（此前测试只读 claims，未验证签名）。</summary>
public sealed class JwtSignatureValidationTests
{
    private const string SigningKey = "shift-scheduling-tests-signing-key-at-least-32-bytes";

    private static TokenValidationParameters BuildValidationParameters()
        => new()
        {
            ValidateIssuer = true,
            ValidIssuer = "ShiftScheduling.Api.Tests",
            ValidateAudience = true,
            ValidAudience = "ShiftScheduling.Admin.Tests",
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };

    private static JwtTokenService BuildService()
        => new(Options.Create(new JwtOptions
        {
            Issuer = "ShiftScheduling.Api.Tests",
            Audience = "ShiftScheduling.Admin.Tests",
            SigningKey = SigningKey,
            ExpireMinutes = 30
        }));

    [Fact]
    public void CreateToken_TokenPassesCryptographicSignatureValidation()
    {
        var result = BuildService().CreateToken(new CurrentUserResponse(7, 3, "manager", "门店经理", "STORE_MANAGER", 1));

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(result.Token, BuildValidationParameters(), out _);

        Assert.Equal("7", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("STORE_MANAGER", principal.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void CreateToken_TamperedTokenFailsSignatureValidation()
    {
        var result = BuildService().CreateToken(new CurrentUserResponse(7, 3, "manager", "门店经理", "STORE_MANAGER", 1));

        // 篡改签名段最后一个字符 → 签名校验必须失败
        var parts = result.Token.Split('.');
        Assert.Equal(3, parts.Length);
        var signature = parts[2];
        parts[2] = signature.Substring(0, signature.Length - 1) + (signature.EndsWith("A") ? "B" : "A");
        var tampered = string.Join('.', parts);

        var handler = new JwtSecurityTokenHandler();
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(tampered, BuildValidationParameters(), out _));
    }
}
