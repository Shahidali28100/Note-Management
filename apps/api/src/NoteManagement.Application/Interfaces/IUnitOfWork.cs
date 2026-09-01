namespace NoteManagement.Application.Interfaces;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> inside an EF Core transaction (AGENTS.md §6 — "auth
    /// writes... run inside an EF Core transaction"), committing only if it completes without
    /// throwing. The operation is responsible for calling <see cref="SaveChangesAsync"/> itself
    /// at the point(s) it needs changes persisted.
    /// </summary>
    Task RunInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
