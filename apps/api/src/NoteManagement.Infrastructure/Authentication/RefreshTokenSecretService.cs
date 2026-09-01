using System.Security.Cryptography;
using System.Text;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Infrastructure.Authentication;

/// <summary>Raw refresh-token generation + hashing (SDS §28, spec "Refresh Token Issuance and Storage"). Stateless — safe as a singleton.</summary>
public sealed class RefreshTokenSecretService : IRefreshTokenSecretService
{
    private const int RawTokenSizeInBytes = 32; // 256 bits of entropy

    public string GenerateRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(RawTokenSizeInBytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    // Convert.ToHexStringLower isn't available until .NET 9 — this project targets net8.0.
    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
