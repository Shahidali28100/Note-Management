namespace NoteManagement.Application.Interfaces;

/// <summary>
/// Application-layer abstraction over "can we reach the configured database" — the concrete
/// implementation (which actually touches <c>ApplicationDbContext</c>) lives in Infrastructure,
/// per SDS §5.2/§5.4 (Application calls persistence abstractions; Infrastructure implements them).
/// </summary>
public interface IDatabaseHealthChecker
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
