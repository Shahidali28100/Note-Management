using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Domain;

[TestClass]
public sealed class RefreshTokenTests
{
    [TestMethod]
    public void IsActive_WhenNotRevokedAndNotExpired_ReturnsTrue()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(7));

        Assert.IsTrue(token.IsActive);
    }

    [TestMethod]
    public void IsActive_WhenRevoked_ReturnsFalse()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(7));

        token.Revoke();

        Assert.IsFalse(token.IsActive);
    }

    [TestMethod]
    public void IsActive_WhenExpired_ReturnsFalse()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddSeconds(-1));

        Assert.IsFalse(token.IsActive);
    }

    [TestMethod]
    public void Revoke_WhenCalledTwice_KeepsFirstRevocationTimestamp()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(7));

        token.Revoke();
        var firstRevokedAt = token.RevokedAt;

        token.Revoke();

        Assert.AreEqual(firstRevokedAt, token.RevokedAt);
    }
}
