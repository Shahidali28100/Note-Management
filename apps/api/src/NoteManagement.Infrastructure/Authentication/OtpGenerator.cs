using System.Security.Cryptography;
using System.Text;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Infrastructure.Authentication;

/// <summary>Raw OTP generation + hashing (AB-1003, spec "Forgot Password"). Stateless — safe as a singleton.</summary>
public sealed class OtpGenerator : IOtpGenerator
{
    private const int OtpExclusiveUpperBound = 1_000_000; // 6 digits: 000000–999999

    public string GenerateRawOtp() =>
        RandomNumberGenerator.GetInt32(OtpExclusiveUpperBound).ToString("D6");

    // Same SHA-256-hex shape as RefreshTokenSecretService.Hash — Convert.ToHexStringLower isn't
    // available until .NET 9, this project targets net8.0.
    public string Hash(string rawOtp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawOtp))).ToLowerInvariant();
}
