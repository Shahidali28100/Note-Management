using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Application.DTOs.Auth;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>FRS-AUTH-001. Does not auto-login — the client calls Login separately.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Register(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _authService.RegisterAsync(request, cancellationToken);
        // No GET-by-id endpoint exists to CreatedAtAction against.
        return StatusCode(StatusCodes.Status201Created, user);
    }

    /// <summary>FRS-AUTH-002.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensDto>> Login(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authService.LoginAsync(request, cancellationToken);
        return Ok(tokens);
    }

    /// <summary>FRS-AUTH-003. Anonymous — the refresh token itself is the credential.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensDto>> Refresh(RefreshRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authService.RefreshAsync(request, cancellationToken);
        return Ok(tokens);
    }

    /// <summary>FRS-AUTH-004. Anonymous — possession of the still-valid refresh token is itself the credential, so a client with an expired access token can still log out.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(LogoutRequestDto request, CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Added by AB-1002 so JWT access-token validation has a real protected endpoint to prove
    /// itself against end-to-end. Explicit [Authorize] — ASP.NET Core endpoints are anonymous
    /// by default and no global FallbackPolicy is configured in Program.cs.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken cancellationToken)
    {
        var subClaim = User.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing its 'sub' claim.");
        var userId = Guid.Parse(subClaim);

        var user = await _authService.GetCurrentUserAsync(userId, cancellationToken);
        return Ok(user);
    }

    /// <summary>FRS-AUTH-005. Always 200 — see AuthService.ForgotPasswordAsync's remarks on why.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MessageResponseDto>> ForgotPassword(ForgotPasswordRequestDto request, CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request, cancellationToken);
        return Ok(new MessageResponseDto("If that email is registered, a password reset code has been sent."));
    }

    /// <summary>FRS-AUTH-006.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MessageResponseDto>> ResetPassword(ResetPasswordRequestDto request, CancellationToken cancellationToken)
    {
        await _authService.ResetPasswordAsync(request, cancellationToken);
        return Ok(new MessageResponseDto("Password has been reset successfully."));
    }
}
