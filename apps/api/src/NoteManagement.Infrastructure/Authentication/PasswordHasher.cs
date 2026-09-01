using Microsoft.AspNetCore.Identity;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Authentication;

/// <summary>Wraps ASP.NET Core Identity's PasswordHasher (PBKDF2-HMACSHA256) — no hand-rolled crypto (SDS §61).</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<User> _inner = new();

    // The wrapped hasher only needs a User instance to satisfy its generic constraint — it does
    // not read any of the user's properties, so a placeholder is safe here.
    private static readonly User HasherUserPlaceholder = User.Register(string.Empty, string.Empty, string.Empty);

    public string Hash(string password) => _inner.HashPassword(HasherUserPlaceholder, password);

    public bool Verify(string password, string passwordHash) =>
        _inner.VerifyHashedPassword(HasherUserPlaceholder, passwordHash, password) != PasswordVerificationResult.Failed;
}
