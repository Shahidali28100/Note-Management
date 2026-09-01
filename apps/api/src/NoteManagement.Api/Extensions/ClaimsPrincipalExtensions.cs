using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NoteManagement.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Same 'sub'-claim extraction AuthController.GetMe already performs inline — factored out
    /// here since every NotesController action needs it. Throws if called on a request that
    /// reached an [Authorize]-protected action without a 'sub' claim, which should never happen
    /// (see AuthController.GetMe's identical precedent).
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var subClaim = user.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing its 'sub' claim.");
        return Guid.Parse(subClaim);
    }
}
