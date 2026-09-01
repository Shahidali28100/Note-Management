using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Interfaces;

public interface IPasswordResetOtpRepository
{
    void Add(PasswordResetOtp otp);

    /// <summary>Most recently created OTP for this user, regardless of used/expired state — backs the 60s reissue cooldown, which must key off "last issued", not "last active".</summary>
    Task<PasswordResetOtp?> GetLatestForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The currently valid (unused, unexpired) OTP for this user, if any — at most one should ever exist, by construction. Backs reset-password validation and attempt counting.</summary>
    Task<PasswordResetOtp?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomic bulk UPDATE — marks every currently-unused OTP row for this user as used, in one
    /// statement (SDS §47's no-read-modify-write principle, same shape as
    /// IRefreshTokenRepository.RevokeAllActiveForUserAsync). Reused for two different business
    /// moments: superseding a prior OTP when a new one is issued, and consuming-plus-invalidating
    /// everything-else on a successful reset.
    /// </summary>
    Task InvalidateAllActiveForUserAsync(Guid userId, CancellationToken cancellationToken);
}
