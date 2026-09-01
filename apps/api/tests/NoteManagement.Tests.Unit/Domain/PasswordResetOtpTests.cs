using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Domain;

[TestClass]
public sealed class PasswordResetOtpTests
{
    [TestMethod]
    public void IsActive_WhenNotUsedAndNotExpired_ReturnsTrue()
    {
        var otp = PasswordResetOtp.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);

        Assert.IsTrue(otp.IsActive);
    }

    [TestMethod]
    public void IsActive_WhenUsed_ReturnsFalse()
    {
        var otp = PasswordResetOtp.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);

        otp.Invalidate();

        Assert.IsFalse(otp.IsActive);
    }

    [TestMethod]
    public void IsActive_WhenExpired_ReturnsFalse()
    {
        var otp = PasswordResetOtp.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(-11));

        Assert.IsFalse(otp.IsActive);
    }

    [TestMethod]
    public void RegisterFailedAttempt_BelowMaxAttempts_DoesNotInvalidate()
    {
        var otp = PasswordResetOtp.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);

        for (var i = 0; i < PasswordResetOtp.MaxAttempts - 1; i++)
        {
            otp.RegisterFailedAttempt();
        }

        Assert.AreEqual(PasswordResetOtp.MaxAttempts - 1, otp.AttemptCount);
        Assert.IsTrue(otp.IsActive);
    }

    [TestMethod]
    public void RegisterFailedAttempt_ReachingMaxAttempts_Invalidates()
    {
        var otp = PasswordResetOtp.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);

        for (var i = 0; i < PasswordResetOtp.MaxAttempts; i++)
        {
            otp.RegisterFailedAttempt();
        }

        Assert.AreEqual(PasswordResetOtp.MaxAttempts, otp.AttemptCount);
        Assert.IsFalse(otp.IsActive);
        Assert.IsNotNull(otp.UsedAt);
    }

    [TestMethod]
    public void Invalidate_WhenCalledTwice_KeepsFirstTimestamp()
    {
        var otp = PasswordResetOtp.Issue(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);

        otp.Invalidate();
        var firstUsedAt = otp.UsedAt;

        otp.Invalidate();

        Assert.AreEqual(firstUsedAt, otp.UsedAt);
    }
}
