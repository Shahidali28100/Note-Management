namespace NoteManagement.Domain.Entities;

/// <summary>
/// A DB-backed refresh token (AB-1002 / FRS-AUTH-003, SDS §11). Only the hash is ever
/// persisted — the raw token is returned to the client exactly once, at issuance.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Issue"/>.</summary>
    private RefreshToken()
    {
    }

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// Domain rule shared by the refresh and reuse-detection flows: a token is usable only if
    /// it hasn't been revoked (by rotation, logout, or reuse-detection cascade) and hasn't
    /// naturally expired.
    /// </summary>
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    /// <summary>Idempotent — revoking an already-revoked token does not move its RevokedAt timestamp.</summary>
    public void Revoke() => RevokedAt ??= DateTime.UtcNow;
}
