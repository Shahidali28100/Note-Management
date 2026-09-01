using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Interfaces;

public interface IRefreshTokenRepository
{
    void Add(RefreshToken token);

    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Atomic bulk revoke (single UPDATE, no read-modify-write — SDS §47's ViewCount principle
    /// applied here) of every currently active token for a user. Used by the reuse-detection
    /// cascade when an already-revoked token is presented to /refresh.
    /// </summary>
    Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken cancellationToken);
}
