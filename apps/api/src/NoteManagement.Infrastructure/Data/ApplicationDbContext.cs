using Microsoft.EntityFrameworkCore;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Data;

/// <summary>
/// The application's single EF Core DbContext (SDS §6). AB-1002 introduced the first entities
/// (Users, RefreshTokens); AB-1003 added PasswordResetOtps; AB-1004 adds Notes; Tags/etc. follow
/// in AB-1005+. Adding a DbSet beyond an approved ticket's scope would violate AGENTS.md §11.
/// </summary>
public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<NoteTag> NoteTags => Set<NoteTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
