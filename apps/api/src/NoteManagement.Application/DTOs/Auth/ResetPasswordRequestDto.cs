using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Auth;

/// <summary>FRS-AUTH-006. Attributes target the constructor parameters — see RegisterRequestDto's remarks.</summary>
public sealed record ResetPasswordRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Code must be 6 digits.")] string Otp,
    [Required, MinLength(8), PasswordPolicy] string NewPassword);
