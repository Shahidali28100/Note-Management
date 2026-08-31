using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.DTOs.Auth;

/// <summary>FRS-AUTH-002. Attributes target the constructor parameters — see RegisterRequestDto's remarks.</summary>
public sealed record LoginRequestDto(
    [Required, EmailAddress] string Email,
    [Required] string Password);
