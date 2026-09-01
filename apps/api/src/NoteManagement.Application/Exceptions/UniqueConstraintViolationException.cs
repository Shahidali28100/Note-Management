namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Generic, reusable signal that a write violated a unique index/constraint — thrown by
/// <c>IUnitOfWork.SaveChangesAsync</c> (Infrastructure) instead of leaking the EF Core
/// <c>DbUpdateException</c> type across the Application/Infrastructure boundary (Application
/// has zero EF Core dependency by design). Callers translate this into a business-specific
/// exception for their own context — e.g. AuthService.RegisterAsync rethrows it as
/// <see cref="DuplicateEmailException"/>. Not itself mapped to an HTTP status by
/// ProblemDetailsExceptionHandler; a caller that doesn't translate it falls through to 500,
/// which is correct for a constraint violation nobody expected.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(Exception innerException)
        : base("A unique constraint was violated.", innerException)
    {
    }
}
