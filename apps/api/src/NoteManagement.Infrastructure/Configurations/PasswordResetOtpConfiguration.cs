using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Configurations;

public sealed class PasswordResetOtpConfiguration : IEntityTypeConfiguration<PasswordResetOtp>
{
    public void Configure(EntityTypeBuilder<PasswordResetOtp> builder)
    {
        builder.ToTable("PasswordResetOtps");

        builder.HasKey(o => o.Id);

        // SHA-256 hex is 64 chars — headroom kept, same as RefreshTokenConfiguration.TokenHash.
        // Deliberately NOT unique (unlike RefreshTokens.TokenHash): a 6-digit OTP has only
        // 1,000,000 possible values, so hash collisions across different users are expected
        // over time and are not a uniqueness violation.
        builder.Property(o => o.OtpHash)
            .HasMaxLength(128)
            .IsRequired();

        // Supports GetActiveForUserAsync's WHERE UserId = X AND UsedAt IS NULL AND ExpiresAt > now,
        // and the InvalidateAllActiveForUserAsync bulk update — same shape as
        // RefreshTokenConfiguration's (UserId, RevokedAt) index.
        builder.HasIndex(o => new { o.UserId, o.UsedAt });

        builder.Property(o => o.ExpiresAt)
            .IsRequired();

        builder.Property(o => o.AttemptCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // UsedAt is nullable by default (DateTime?) — no IsRequired() call needed.

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
