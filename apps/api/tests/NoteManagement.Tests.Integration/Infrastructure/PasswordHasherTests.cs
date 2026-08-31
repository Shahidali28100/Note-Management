using NoteManagement.Infrastructure.Authentication;

namespace NoteManagement.Tests.Integration.Infrastructure;

/// <summary>No DB needed — lives here only for the Infrastructure project reference (see JwtTokenGeneratorTests' remarks).</summary>
[TestClass]
public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [TestMethod]
    public void Hash_ThenVerify_WithCorrectPassword_ReturnsTrue()
    {
        var hash = _sut.Hash("Passw0rd");

        Assert.IsTrue(_sut.Verify("Passw0rd", hash));
    }

    [TestMethod]
    public void Verify_WithIncorrectPassword_ReturnsFalse()
    {
        var hash = _sut.Hash("Passw0rd");

        Assert.IsFalse(_sut.Verify("WrongPassword1", hash));
    }
}
