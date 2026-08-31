using NoteManagement.Infrastructure.Authentication;

namespace NoteManagement.Tests.Integration.Infrastructure;

/// <summary>No DB needed — lives here only for the Infrastructure project reference (see JwtTokenGeneratorTests' remarks).</summary>
[TestClass]
public sealed class RefreshTokenSecretServiceTests
{
    private readonly RefreshTokenSecretService _sut = new();

    [TestMethod]
    public void GenerateRawToken_ProducesUniqueHighEntropyValues()
    {
        var first = _sut.GenerateRawToken();
        var second = _sut.GenerateRawToken();

        Assert.AreNotEqual(first, second);
        Assert.IsTrue(first.Length >= 32); // base64url of 32 bytes is well over 32 chars
    }

    [TestMethod]
    public void Hash_IsDeterministic_AndDiffersFromRawToken()
    {
        var rawToken = _sut.GenerateRawToken();

        var firstHash = _sut.Hash(rawToken);
        var secondHash = _sut.Hash(rawToken);

        Assert.AreEqual(firstHash, secondHash);
        Assert.AreNotEqual(rawToken, firstHash);
    }
}
