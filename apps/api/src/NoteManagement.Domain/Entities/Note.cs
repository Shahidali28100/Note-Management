namespace NoteManagement.Domain.Entities;

/// <summary>
/// A user's note (AB-1004 / FRS-NOTE-001..005, SDS §13). Content is stored as an opaque string —
/// no structural/format validation or interpretation (proposal.md: the TipTap representation is
/// an AB-1012 decision). Zero ASP.NET Core dependency.
/// </summary>
public sealed class Note
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Create"/>.</summary>
    private Note()
    {
    }

    public static Note Create(Guid userId, string title, string content)
    {
        var now = DateTime.UtcNow;
        return new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>FRS-NOTE-003: full replace of title/content; bumps UpdatedAt, leaves CreatedAt untouched.</summary>
    public void UpdateContent(string title, string content)
    {
        Title = title;
        Content = content;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// FRS-NOTE-004. Idempotent at the domain level (mirrors RefreshToken.Revoke()) — NoteService
    /// still rejects a redundant call with NoteNotFoundException per the spec's "already-deleted
    /// → 404" scenario before this is ever reached twice for the same note.
    /// </summary>
    public void SoftDelete() => DeletedAt ??= DateTime.UtcNow;

    /// <summary>
    /// FRS-NOTE-005. NoteService checks IsDeleted before calling this (throws
    /// NoteNotDeletedException otherwise), so this is only ever invoked on a currently-deleted note.
    /// </summary>
    public void Restore() => DeletedAt = null;
}
