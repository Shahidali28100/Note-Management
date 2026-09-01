namespace NoteManagement.Application.DTOs.Auth;

/// <summary>Returned by login and refresh (FRS-AUTH-002/003). RefreshToken is the raw, single-use value — returned exactly once.</summary>
public sealed record AuthTokensDto(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc, string TokenType = "Bearer");
