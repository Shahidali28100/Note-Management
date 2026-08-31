namespace NoteManagement.Application.Interfaces;

public interface IJwtTokenGenerator
{
    /// <summary>Signs a new HS256 access token with <c>sub</c> = <paramref name="userId"/>.</summary>
    (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(Guid userId);
}
