namespace NoteManagement.Domain.Entities;

/// <summary>
/// A registered account (AB-1002 / FRS-AUTH-001, SDS §10). Zero ASP.NET Core dependency.
/// </summary>
public sealed class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Register"/>.</summary>
    private User()
    {
    }

    public static User Register(string name, string email, string passwordHash)
    {
        var now = DateTime.UtcNow;
        return new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = passwordHash,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>AB-1003 / FRS-AUTH-006: sets a new password hash after a successful OTP-based reset.</summary>
    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        UpdatedAt = DateTime.UtcNow;
    }
}
