namespace NoteManagement.Application.DTOs.Auth;

/// <summary>Generic acknowledgement shared by forgot-password and reset-password's 200 responses (AB-1003).</summary>
public sealed record MessageResponseDto(string Message);
