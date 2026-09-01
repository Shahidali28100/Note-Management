namespace NoteManagement.Domain.Entities;

/// <summary>
/// A password-reset OTP (AB-1003 / FRS-AUTH-005/006, SDS §12 + AttemptCount — see proposal.md
/// Impact). Only the hash is ever persisted; the raw 6-digit code is handed to the caller
/// exactly once (to log), never stored or returned via the API.
///
/// UsedAt is reused as the single "no longer usable" flag for three distinct business events —
/// a successful reset, being superseded by a newer OTP, and being locked out after too many
/// incorrect attempts — because all three share the same externally observable behavior (the
/// code stops working), and SDS §12's baseline schema has no room for a separate "why" column.
/// </summary>
public sealed class PasswordResetOtp
{
    public const int MaxAttempts = 5;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string OtpHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Issue"/>.</summary>
    private PasswordResetOtp()
    {
    }

    /// <summary>
    /// Both timestamps are caller-supplied (mirrors RefreshToken.Issue's explicit `expiresAt`)
    /// rather than reading DateTime.UtcNow internally for CreatedAt — AuthService.ForgotPasswordAsync
    /// computes `now` once and reuses it for both ExpiresAt and CreatedAt, avoiding two separate
    /// clock reads, and letting tests control CreatedAt directly (needed for the reissue-cooldown
    /// logic, which is keyed off CreatedAt rather than ExpiresAt).
    /// </summary>
    public static PasswordResetOtp Issue(Guid userId, string otpHash, DateTime expiresAt, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        OtpHash = otpHash,
        ExpiresAt = expiresAt,
        CreatedAt = createdAt,
    };

    /// <summary>
    /// Usable only if not yet consumed/invalidated and not yet expired. Attempt-count lockout
    /// does not need its own check here — RegisterFailedAttempt reaching MaxAttempts calls
    /// Invalidate() immediately, so UsedAt already reflects the lockout.
    /// </summary>
    public bool IsActive => UsedAt is null && ExpiresAt > DateTime.UtcNow;

    /// <summary>Idempotent — mirrors RefreshToken.Revoke(). Used for all three "no longer usable" events described above.</summary>
    public void Invalidate() => UsedAt ??= DateTime.UtcNow;

    /// <summary>Increments the incorrect-attempt counter; on the 5th, locks (invalidates) this OTP even though it hasn't expired.</summary>
    public void RegisterFailedAttempt()
    {
        AttemptCount++;
        if (AttemptCount >= MaxAttempts)
        {
            Invalidate();
        }
    }
}
