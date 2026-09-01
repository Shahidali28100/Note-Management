using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.DTOs.Auth;

/// <summary>FRS-AUTH-004. Possession of the still-valid refresh token is itself the credential — no access token required. Attribute targets the constructor parameter — see RegisterRequestDto's remarks.</summary>
public sealed record LogoutRequestDto([Required] string RefreshToken);
