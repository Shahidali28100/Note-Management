namespace NoteManagement.Infrastructure.Authentication;

/// <summary>
/// Application never references this — the project-reference graph (Application has no
/// dependency on Infrastructure) is what actually enforces that, not C# accessibility. Built
/// once from configuration inside <c>AddInfrastructure</c> (Infrastructure/DependencyInjection.cs).
/// Public (rather than internal) so Tests.Integration/Infrastructure can construct
/// <see cref="JwtTokenGenerator"/> directly without an InternalsVisibleTo grant.
/// </summary>
public sealed record JwtOptions(string SigningKey, string Issuer, string Audience, TimeSpan AccessTokenLifetime);
