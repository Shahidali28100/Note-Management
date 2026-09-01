using System.Text.RegularExpressions;
using NoteManagement.Infrastructure.Authentication;

namespace NoteManagement.Tests.Integration.Infrastructure;

/// <summary>No DB needed — lives here only for the Infrastructure project reference (see JwtTokenGeneratorTests' remarks).</summary>
[TestClass]
public sealed class OtpGeneratorTests
{
    private readonly OtpGenerator _sut = new();

    [TestMethod]
    public void GenerateRawOtp_ProducesSixDigitCodes()
    {
        for (var i = 0; i < 20; i++)
        {
            var otp = _sut.GenerateRawOtp();

            Assert.AreEqual(6, otp.Length);
            Assert.IsTrue(Regex.IsMatch(otp, "^[0-9]{6}$"));
        }
    }

    [TestMethod]
    public void Hash_IsDeterministic_AndDiffersFromRawOtp()
    {
        var rawOtp = _sut.GenerateRawOtp();

        var firstHash = _sut.Hash(rawOtp);
        var secondHash = _sut.Hash(rawOtp);

        Assert.AreEqual(firstHash, secondHash);
        Assert.AreNotEqual(rawOtp, firstHash);
    }
}
