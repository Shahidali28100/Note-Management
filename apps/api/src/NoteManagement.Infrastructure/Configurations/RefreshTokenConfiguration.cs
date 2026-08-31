using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(rt => rt.Id);

        // SHA-256 hex is 64 chars — headroom kept.
        builder.Property(rt => rt.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        // Primary lookup path on every /refresh and /logout call.
        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique();

        // Supports the reuse-detection cascade's WHERE UserId = X AND RevokedAt IS NULL query.
        builder.HasIndex(rt => new { rt.UserId, rt.RevokedAt });

        builder.Property(rt => rt.ExpiresAt)
            .IsRequired();

        builder.Property(rt => rt.CreatedAt)
            .IsRequired();

        // RevokedAt is nullable by default (DateTime?) — no IsRequired() call needed.

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
