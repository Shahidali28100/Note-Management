using NoteManagement.Application.Validation;

namespace NoteManagement.Tests.Unit.Application;

[TestClass]
public sealed class PasswordPolicyAttributeTests
{
    private readonly PasswordPolicyAttribute _sut = new();

    [TestMethod]
    [DataRow("noDigitsHere")]
    [DataRow("12345678")]
    [DataRow("")]
    public void IsValid_WithPasswordMissingLetterOrDigit_ReturnsFalse(string password)
    {
        Assert.IsFalse(_sut.IsValid(password));
    }

    [TestMethod]
    public void IsValid_WithPasswordMeetingPolicy_ReturnsTrue()
    {
        Assert.IsTrue(_sut.IsValid("Passw0rd"));
    }

    [TestMethod]
    public void IsValid_WithNull_ReturnsTrue()
    {
        // Null/empty is [Required]'s concern, not this attribute's.
        Assert.IsTrue(_sut.IsValid(null));
    }
}
