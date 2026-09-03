namespace NoteManagement.Domain.Entities;

/// <summary>
/// A user's tag (AB-1006 / FRS-TAG-001..004, SDS §15). Mirrors Note.cs's shape exactly — private
/// setters, static factory, zero ASP.NET Core dependency.
/// </summary>
public sealed class Tag
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Color { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Create"/>.</summary>
    private Tag()
    {
    }

    public static Tag Create(Guid userId, string name, string color)
    {
        var now = DateTime.UtcNow;
        return new Tag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Color = color,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>FRS-TAG-002: full replace of name/color; bumps UpdatedAt. Ownership (UserId) is never touched here.</summary>
    public void Rename(string name, string color)
    {
        Name = name;
        Color = color;
        UpdatedAt = DateTime.UtcNow;
    }
}
