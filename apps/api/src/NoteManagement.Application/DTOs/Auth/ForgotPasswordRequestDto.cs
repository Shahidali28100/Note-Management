using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.DTOs.Auth;

/// <summary>FRS-AUTH-005. Attributes target the constructor parameter — see RegisterRequestDto's remarks.</summary>
public sealed record ForgotPasswordRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email);
