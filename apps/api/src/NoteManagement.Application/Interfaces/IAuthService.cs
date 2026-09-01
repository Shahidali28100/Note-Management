using NoteManagement.Application.DTOs.Auth;

namespace NoteManagement.Application.Interfaces;

public interface IAuthService
{
    Task<UserDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken);

    Task<AuthTokensDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken);

    Task<AuthTokensDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken);

    Task LogoutAsync(LogoutRequestDto request, CancellationToken cancellationToken);

    Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);

    Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken);

    Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken);
}
