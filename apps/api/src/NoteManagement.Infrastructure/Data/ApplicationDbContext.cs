using Microsoft.EntityFrameworkCore;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Data;

/// <summary>
/// The application's single EF Core DbContext (SDS §6). AB-1002 introduced the first entities
/// (Users, RefreshTokens); AB-1003 adds PasswordResetOtps; Notes/Tags/etc. follow in AB-1004+.
/// Adding a DbSet beyond an approved ticket's scope would violate AGENTS.md §11.
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
