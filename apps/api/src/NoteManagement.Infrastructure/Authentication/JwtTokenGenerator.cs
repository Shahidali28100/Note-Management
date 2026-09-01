using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Infrastructure.Authentication;

/// <summary>Signs HS256 access tokens (SDS §27). Stateless — safe as a singleton.</summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGenerator(JwtOptions options)
    {
        _options = options;
    }

    public (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(Guid userId)
    {
        var expiresAtUtc = DateTime.UtcNow.Add(_options.AccessTokenLifetime);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return (accessToken, expiresAtUtc);
    }
}
