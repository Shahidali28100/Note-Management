using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.DTOs.Auth;

/// <summary>FRS-AUTH-003. Attribute targets the constructor parameter — see RegisterRequestDto's remarks.</summary>
public sealed record RefreshRequestDto([Required] string RefreshToken);
