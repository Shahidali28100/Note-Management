namespace NoteManagement.Application.Interfaces;

public interface IOtpGenerator
{
    /// <summary>Cryptographically random 6-digit numeric code, zero-padded (e.g. "048392").</summary>
    string GenerateRawOtp();

    /// <summary>SHA-256 hex — same shape and purpose as IRefreshTokenSecretService.Hash.</summary>
    string Hash(string rawOtp);
}
