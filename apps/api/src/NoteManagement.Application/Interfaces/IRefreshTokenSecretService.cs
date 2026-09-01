namespace NoteManagement.Application.Interfaces;

public interface IRefreshTokenSecretService
{
    /// <summary>Cryptographically random, 256 bits of entropy, base64url-encoded.</summary>
    string GenerateRawToken();

    /// <summary>Deterministic (SHA-256 hex) — used both to store and to look up by hash.</summary>
    string Hash(string rawToken);
}
