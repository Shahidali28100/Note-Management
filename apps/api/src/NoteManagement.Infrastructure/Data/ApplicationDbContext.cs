using Microsoft.EntityFrameworkCore;

namespace NoteManagement.Infrastructure.Data;

/// <summary>
/// The application's single EF Core DbContext (SDS §6). Intentionally has zero
/// <see cref="DbSet{TEntity}"/> properties in AB-1001 — the first entities
/// (Users, RefreshTokens, PasswordResetOtps) are introduced in AB-1002, and
/// Notes/Tags/etc. follow in AB-1004+. Adding a DbSet here without an approved
/// spec change would violate AGENTS.md §11 ("do not introduce entities not
/// covered by an approved ticket").
/// </summary>
public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
}
