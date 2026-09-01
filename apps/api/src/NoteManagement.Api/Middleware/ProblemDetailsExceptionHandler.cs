using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Application.Exceptions;

namespace NoteManagement.Api.Middleware;

/// <summary>
/// Maps known Application-layer exceptions to the Problem Details error contract (SDS §39).
/// Reusable plumbing, not auth-only — future tickets add their own typed exceptions to the
/// mapping below rather than each controller hand-rolling try/catch. Anything not mapped here
/// falls through (returns false) to the generic <c>UseExceptionHandler()</c> 500 already
/// established in AB-1001.
/// </summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            DuplicateEmailException => (StatusCodes.Status409Conflict, "Email already registered"),
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "Invalid credentials"),
            InvalidRefreshTokenException => (StatusCodes.Status401Unauthorized, "Invalid refresh token"),
            InvalidPasswordResetException => (StatusCodes.Status400BadRequest, "Invalid password reset request"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0)
        {
            return ValueTask.FromResult(false);
        }

        return HandleAsync(httpContext, exception, statusCode, title, cancellationToken);
    }

    private static async ValueTask<bool> HandleAsync(HttpContext httpContext, Exception exception, int statusCode, string title, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Type = $"https://httpstatuses.io/{statusCode}",
                Title = title,
                Status = statusCode,
                Detail = exception.Message,
            },
            cancellationToken);

        return true;
    }
}
