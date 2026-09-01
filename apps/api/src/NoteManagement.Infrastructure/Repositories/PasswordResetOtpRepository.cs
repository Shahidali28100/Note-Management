using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;
using NoteManagement.Infrastructure.Data;

namespace NoteManagement.Infrastructure.Repositories;

public sealed class PasswordResetOtpRepository : IPasswordResetOtpRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PasswordResetOtpRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(PasswordResetOtp otp) => _dbContext.PasswordResetOtps.Add(otp);

    public Task<PasswordResetOtp?> GetLatestForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.PasswordResetOtps
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PasswordResetOtp?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _dbContext.PasswordResetOtps
            .Where(o => o.UserId == userId && o.UsedAt == null && o.ExpiresAt > now)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Single atomic UPDATE — no read-modify-write, same shape as RefreshTokenRepository.RevokeAllActiveForUserAsync.</summary>
    public Task InvalidateAllActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _dbContext.PasswordResetOtps
            .Where(o => o.UserId == userId && o.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.UsedAt, now), cancellationToken);
    }
}
