using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Auth;

/// <summary>
/// FRS-AUTH-001. Field shapes match delta-openapi.yaml's RegisterRequest exactly.
/// Validation attributes deliberately target the constructor parameters (no "property:"
/// prefix) — ASP.NET Core's record model-binding validation reads metadata from the primary
/// constructor parameters, not the compiler-generated properties; "property:"-targeted
/// attributes are silently ignored at runtime and throw an InvalidOperationException instead.
/// </summary>
public sealed record RegisterRequestDto(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, MinLength(8), PasswordPolicy] string Password);
