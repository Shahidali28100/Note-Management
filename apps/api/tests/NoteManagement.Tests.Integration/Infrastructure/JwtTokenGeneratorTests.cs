using System.IdentityModel.Tokens.Jwt;
using NoteManagement.Infrastructure.Authentication;

namespace NoteManagement.Tests.Integration.Infrastructure;

/// <summary>
/// No DB/WebApplicationFactory needed — lives here (not Tests.Unit) only because it needs a
/// project reference to NoteManagement.Infrastructure, which Tests.Unit deliberately doesn't
/// have (AB-1001's layering intent). Matches the existing ApplicationDbContextTests precedent.
/// </summary>
[TestClass]
public sealed class JwtTokenGeneratorTests
{
    [TestMethod]
    public void GenerateAccessToken_ReturnsTokenWithSubClaimAndFifteenMinuteExpiry()
    {
        var options = new JwtOptions("a-test-signing-key-at-least-32-bytes-long!!", "TestIssuer", "TestAudience", TimeSpan.FromMinutes(15));
        var sut = new JwtTokenGenerator(options);
        var userId = Guid.NewGuid();

        var (accessToken, expiresAtUtc) = sut.GenerateAccessToken(userId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.AreEqual(userId.ToString(), jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.AreEqual("TestIssuer", jwt.Issuer);
        Assert.AreEqual("TestAudience", jwt.Audiences.Single());
        Assert.IsTrue(expiresAtUtc > DateTime.UtcNow.AddMinutes(14) && expiresAtUtc <= DateTime.UtcNow.AddMinutes(15));
    }
}
