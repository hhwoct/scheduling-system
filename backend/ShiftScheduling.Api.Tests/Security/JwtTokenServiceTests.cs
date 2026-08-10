using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ShiftScheduling.Api.Application.Auth;
using ShiftScheduling.Api.Application.Security;
using ShiftScheduling.Api.Infrastructure;

namespace ShiftScheduling.Api.Tests.Security;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void CreateToken_IncludesRequiredClaimsAndConfiguredLifetime()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "ShiftScheduling.Api.Tests",
            Audience = "ShiftScheduling.Admin.Tests",
            SigningKey = "shift-scheduling-tests-signing-key-at-least-32-bytes",
            ExpireMinutes = 30
        });
        var service = new JwtTokenService(options);

        var result = service.CreateToken(new CurrentUserResponse(7, 3, "manager", "门店经理", "STORE_MANAGER"));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal("ShiftScheduling.Api.Tests", token.Issuer);
        Assert.Contains("ShiftScheduling.Admin.Tests", token.Audiences);
        Assert.Equal("7", token.Claims.Single(x => x.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("3", token.Claims.Single(x => x.Type == "store_id").Value);
        Assert.Equal("manager", token.Claims.Single(x => x.Type == "username").Value);
        Assert.Equal("STORE_MANAGER", token.Claims.Single(x => x.Type == ClaimTypes.Role).Value);
        Assert.False(string.IsNullOrWhiteSpace(token.Id));
        Assert.InRange(result.ExpiresAt, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
    }
}