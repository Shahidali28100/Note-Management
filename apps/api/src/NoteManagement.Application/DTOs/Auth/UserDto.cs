namespace NoteManagement.Application.DTOs.Auth;

/// <summary>Non-sensitive profile shape returned by register and GET /api/auth/me — never a password or hash.</summary>
public sealed record UserDto(Guid Id, string Name, string Email);
