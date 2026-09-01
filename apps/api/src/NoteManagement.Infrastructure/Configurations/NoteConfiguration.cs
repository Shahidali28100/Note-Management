using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Configurations;

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title)
            .HasMaxLength(200)
            .IsRequired();

        // No HasMaxLength — nvarchar(max), opaque per the AB-1004 content-format decision
        // (proposal.md). No structural/format validation at this layer either.
        builder.Property(n => n.Content)
            .IsRequired();

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.Property(n => n.UpdatedAt)
            .IsRequired();

        // DeletedAt is nullable by default — no IsRequired() call needed.

        // Supports GetPageForUserAsync's WHERE UserId = X (filter narrows to DeletedAt IS NULL)
        // ORDER BY UpdatedAt DESC, and GetByIdAsync/GetByIdIncludingDeletedAsync's
        // WHERE Id = X AND UserId = Y — same composite-index idiom as
        // PasswordResetOtpConfiguration's (UserId, UsedAt).
        builder.HasIndex(n => new { n.UserId, n.DeletedAt, n.UpdatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // FRS-NOTE-004/SDS §14: normal queries exclude soft-deleted rows by default.
        // GetByIdIncludingDeletedAsync explicitly calls IgnoreQueryFilters() to see past this.
        builder.HasQueryFilter(n => n.DeletedAt == null);
    }
}
